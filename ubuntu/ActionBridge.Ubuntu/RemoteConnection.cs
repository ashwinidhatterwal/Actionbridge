using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ActionBridge.Core;
namespace ActionBridge.Ubuntu;
public sealed record RemoteConfig(string Service,string Room,string PcKey,string PhoneKey,string Secret,string Folder);
public sealed class RemoteConnection:IDisposable {
 public const string Service="https://actionbridge-connect.actionbridge.workers.dev";
 readonly string path,folder,id,pin;readonly Transfers transfers;readonly IActionSink sink;readonly Outbox outbox;readonly SemaphoreSlim configureGate=new(1,1),pipeGate=new(1,1);readonly object helperGate=new();Process? helper;RemoteJobs? jobs;bool disposed;
 public RemoteConfig? Config {get;private set;}
 public string Status {get;private set;}="Internet connection is starting…";
 public event Action? Changed;
 public RemoteConnection(string root,string received,string id,string pin,Transfers transfers,IActionSink sink,Outbox outbox){path=Path.Combine(root,"remote.json");folder=Path.Combine(received,"Remote");this.id=id;this.pin=pin;this.transfers=transfers;this.sink=sink;this.outbox=outbox;if(File.Exists(path)){Config=JsonSerializer.Deserialize<RemoteConfig>(File.ReadAllText(path),Wire.Json)??throw new InvalidDataException("Remote settings are unreadable.");if(Config.Service!=Service||!Keys(Config.PcKey)||!Keys(Config.PhoneKey)||!Keys(Config.Secret)||Config.Room.Length!=32||!Config.Room.All(Uri.IsHexDigit))throw new InvalidDataException("Remote settings are invalid. Restore a private backup before reconnecting.");}}
 static bool Keys(string key)=>key.Length==43&&key.All(c=>char.IsAsciiLetterOrDigit(c)||c is '_' or '-');
 static string Key()=>Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+','-').Replace('/','_');
 void Report(string value){Status=value;Changed?.Invoke();}
 public bool Enabled=>!File.Exists(Path.Combine(Path.GetDirectoryName(path)!,"remote.disabled"));
 public async Task Configure(bool enable=false){await configureGate.WaitAsync();try{if(disposed)return;if(!Enabled&&!enable)return;if(enable)File.Delete(Path.Combine(Path.GetDirectoryName(path)!,"remote.disabled"));if(Config!=null){Start();return;}
  Report("Setting up internet access…");var seedPath=path+".enroll";string[] seeds;if(File.Exists(seedPath)){seeds=JsonSerializer.Deserialize<string[]>(File.ReadAllText(seedPath))!;if(seeds.Length!=2||!seeds.All(Keys))throw new InvalidDataException("Enrollment identity is damaged. Restore a private backup.");}else{seeds=new[]{Key(),Key()};Wire.AtomicJson(seedPath,seeds);}
  using var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(20)};using var result=await client.PostAsync(Service+"/enroll",new StringContent(JsonSerializer.Serialize(new{pcKey=seeds[0],phoneKey=seeds[1]}),Encoding.UTF8,"application/json"));
  if(!result.IsSuccessStatusCode)throw new InvalidOperationException(result.StatusCode==System.Net.HttpStatusCode.TooManyRequests?"Remote setup is busy. It will retry shortly.":"Internet setup unavailable. Local transfers still work; retry when online.");
  var raw=await result.Content.ReadAsStringAsync();if(raw.Length>4096)throw new InvalidDataException("Invalid enrollment response.");using var body=JsonDocument.Parse(raw);var room=body.RootElement.GetProperty("room").GetString()!;if(room.Length!=32||!room.All(Uri.IsHexDigit))throw new InvalidDataException("Invalid enrollment response.");
  var configured=new RemoteConfig(Service,room,seeds[0],seeds[1],Key(),folder);Wire.AtomicJson(path,configured);Config=configured;Start();Report("Internet access ready · Connect phone to pair");
 }catch(Exception e){Report(e.Message);throw;}finally{configureGate.Release();}}
 public void Start(){lock(helperGate){if(disposed||!Enabled||Config==null||helper is {HasExited:false})return;jobs=new(transfers,sink,Config.Room,outbox);helper?.Dispose();var executable=Path.Combine(AppContext.BaseDirectory,"ActionBridge.Remote");
  if(!File.Exists(executable)){Report("Remote helper is missing. Reinstall the Ubuntu package.");return;}
  var process=new Process{StartInfo=new(executable){UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true},EnableRaisingEvents=true};helper=process;var dispatcher=jobs;
  process.OutputDataReceived+=async(_,e)=>{if(e.Data==null)return;try{using var doc=JsonDocument.Parse(e.Data);var m=doc.RootElement;if(m.TryGetProperty("rpcId",out var rpc)){object response;try{response=new{rpcId=rpc.GetString(),result=await dispatcher.Dispatch(m)};}catch(Exception ex){response=new{rpcId=rpc.GetString(),error=ex is ArgumentException or InvalidOperationException or IOException or KeyNotFoundException?ex.Message:"Computer could not complete the action."};}await pipeGate.WaitAsync();try{if(!process.HasExited){await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(response,Wire.JsonCompact));await process.StandardInput.FlushAsync();}}finally{pipeGate.Release();}}
   else if(m.TryGetProperty("detail",out var detail))Report(detail.GetString()??"Connected");}catch{}};
  process.ErrorDataReceived+=(_,_)=>{};process.Exited+=(_,_)=>{if(!disposed)Report("Internet receiver stopped. Reconnecting shortly…");};
  process.Start();process.BeginOutputReadLine();process.BeginErrorReadLine();process.StandardInput.WriteLine(JsonSerializer.Serialize(new{service=Config.Service,room=Config.Room,pcKey=Config.PcKey,secret=Config.Secret,folder=Config.Folder,parent=Environment.ProcessId,jobs=true}));process.StandardInput.Flush();
 }}
 public string PairingCode(){if(Config==null)throw new InvalidOperationException("Internet setup is not ready yet. Retry when connected.");var bytes=JsonSerializer.SerializeToUtf8Bytes(new{v=2,id,fingerprint=pin,service=Config.Service,room=Config.Room,key=Config.PhoneKey,secret=Config.Secret,name=Environment.MachineName});return "abremote:"+Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_');}
 public async Task Disconnect(){await configureGate.WaitAsync();try{if(Config==null){Wire.AtomicJson(Path.Combine(Path.GetDirectoryName(path)!,"remote.disabled"),true);return;}using var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(15)};using var r=new HttpRequestMessage(HttpMethod.Delete,Service+"/rooms/"+Config.Room+"/delete?role=pc");r.Headers.Authorization=new AuthenticationHeaderValue("Bearer",Config.PcKey);using var response=await client.SendAsync(r);if(!response.IsSuccessStatusCode&&response.StatusCode!=System.Net.HttpStatusCode.NotFound)throw new InvalidOperationException("Cannot revoke remote access while offline. Try again when online.");Wire.AtomicJson(Path.Combine(Path.GetDirectoryName(path)!,"remote.disabled"),true);Stop();Config=null;File.Delete(path);File.Delete(path+".enroll");Report("Remote phone access removed. Use Connect phone to pair again.");}finally{configureGate.Release();}}
 void Stop(){lock(helperGate){if(helper==null)return;try{helper.StandardInput.Close();if(!helper.WaitForExit(1500))helper.Kill(true);}catch{}helper.Dispose();helper=null;}}
 public void Dispose(){disposed=true;Stop();}
}
