using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using QRCoder;
using ActionBridge.Core;
namespace ActionBridge.Windows;
public sealed class RemoteConnection:IDisposable {
    public const string DefaultService="https://actionbridge-connect.actionbridge.workers.dev";
    readonly string path,folder;readonly SemaphoreSlim configGate=new(1,1);Process? helper;readonly SemaphoreSlim pipeGate=new(1,1);RemoteJobs? jobs;Outbox? outbox;string pcId="",fingerprint="";
    public event Action<string>? Changed;
    public RemoteConfig? Config {get;private set;}
    public RemoteConnection(string root,string received){path=Path.Combine(root,"remote.dat");folder=Path.Combine(received,"Remote");if(File.Exists(path))try{Config=JsonSerializer.Deserialize<RemoteConfig>(ProtectedData.Unprotect(File.ReadAllBytes(path),null,DataProtectionScope.CurrentUser));}catch{Changed?.Invoke("Remote settings could not be read. Configure again.");}}
    public bool IsConnected {get;private set;}
    public bool HasLinkedDevice=>Config!=null&&File.Exists(path+".linked");
 public bool Enabled=>!File.Exists(path+".disabled");
    static void Store(string target,byte[] bytes){using(var f=new FileStream(target+".new",FileMode.Create,FileAccess.Write,FileShare.None)){f.Write(bytes);f.Flush(true);}File.Move(target+".new",target,true);}
    static string Key()=>Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+','-').Replace('/','_');
    public async Task Configure(string service=DefaultService,string setupKey=""){
        await configGate.WaitAsync();try {
        if(Config!=null)throw new InvalidOperationException("Disconnect the existing remote connection first.");
        if(!Uri.TryCreate(service.Trim(),UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.UserInfo!=""||uri.AbsolutePath!="/"||uri.Query!=""||uri.Fragment!="")throw new ArgumentException("Enter the HTTPS service address, such as https://actionbridge-connect.example.workers.dev");
        service=uri.GetLeftPart(UriPartial.Authority);using var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(20)};var automatic=string.IsNullOrWhiteSpace(setupKey);
        string pcKey="",phoneKey="";
        if(automatic){var seedPath=path+".enroll";
            if(File.Exists(seedPath)){var seeds=JsonSerializer.Deserialize<string[]>(ProtectedData.Unprotect(File.ReadAllBytes(seedPath),null,DataProtectionScope.CurrentUser))!;pcKey=seeds[0];phoneKey=seeds[1];}
            else{pcKey=Key();phoneKey=Key();Directory.CreateDirectory(Path.GetDirectoryName(path)!);Store(seedPath,ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(new[]{pcKey,phoneKey}),null,DataProtectionScope.CurrentUser));}}
        using var request=new HttpRequestMessage(HttpMethod.Post,service+(automatic?"/enroll":"/rooms"));if(!automatic)request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",setupKey.Trim());request.Content=new StringContent(automatic?JsonSerializer.Serialize(new{pcKey,phoneKey}):"{}",Encoding.UTF8,"application/json");using var result=await client.SendAsync(request);if(!result.IsSuccessStatusCode)throw new InvalidOperationException(automatic?"Remote setup is temporarily unavailable. Check your internet and retry. Local transfers still work; internet setup retries automatically.":"Service refused setup. Check the setup key and registration setting.");using var body=JsonDocument.Parse(await result.Content.ReadAsStringAsync());var value=body.RootElement;
        var configured=new RemoteConfig(service,value.GetProperty("room").GetString()!,automatic?pcKey:value.GetProperty("pcKey").GetString()!,automatic?phoneKey:value.GetProperty("phoneKey").GetString()!,Key(),folder);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);Store(path,ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(configured),null,DataProtectionScope.CurrentUser));Config=configured;File.Delete(path+".disabled");Start();
        }finally{configGate.Release();}
    }
    public void Attach(Transfers transfers,IActionSink sink,string id,string pin,Outbox? outgoing=null){pcId=id;fingerprint=pin;outbox=outgoing;if(Config!=null)jobs=new(transfers,sink,Config.Room,outbox);_transfers=transfers;_sink=sink;}
    Transfers? _transfers;IActionSink? _sink;
    public void Start(){if(!Enabled)return;if(Config!=null&&_transfers!=null&&_sink!=null)jobs=new(_transfers,_sink,Config.Room,outbox);if(Config==null||helper is {HasExited:false})return;var exe=Path.Combine(AppContext.BaseDirectory,"ActionBridge.Remote.exe");if(!File.Exists(exe)){Changed?.Invoke("Remote receiver component is missing.");return;}
        helper?.Dispose();
        helper=new Process{StartInfo=new(exe){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true},EnableRaisingEvents=true};
        var process=helper;var dispatcher=jobs;
        helper.OutputDataReceived+=async(s,e)=>{if(e.Data==null)return;try{using var j=JsonDocument.Parse(e.Data);var m=j.RootElement;if(m.TryGetProperty("rpcId",out var rpc)){object result;try{if(dispatcher==null)throw new InvalidOperationException("PC receiver is starting. Reconnect shortly.");result=new{rpcId=rpc.GetString(),result=await dispatcher.Dispatch(m)};}catch(Exception ex){result=new{rpcId=rpc.GetString(),error=ex is ArgumentException or InvalidOperationException or IOException or KeyNotFoundException?ex.Message:"PC could not complete this action."};}
                await pipeGate.WaitAsync();try{if(!process.HasExited){await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(result,Wire.JsonCompact));await process.StandardInput.FlushAsync();}}finally{pipeGate.Release();}
            }else {var detail=m.GetProperty("detail").GetString()??"Internet ready";if(m.TryGetProperty("state",out var kind)&&kind.GetString()=="connection"){IsConnected=detail=="connected";if(IsConnected)Wire.AtomicJson(path+".linked",true);}Changed?.Invoke(detail);}}catch{}};
        helper.Exited+=(s,e)=>{IsConnected=false;Changed?.Invoke("Remote receiver stopped. Reopen the app to reconnect.");};helper.Start();helper.BeginOutputReadLine();helper.StandardInput.WriteLine(JsonSerializer.Serialize(new{service=Config.Service,room=Config.Room,pcKey=Config.PcKey,secret=Config.Secret,folder=Config.Folder,parent=Environment.ProcessId,jobs=true}));helper.StandardInput.Flush();helper.BeginErrorReadLine();
    }
    public string PairingCode(){if(Config==null)throw new InvalidOperationException("Set up remote access first.");var payload=JsonSerializer.SerializeToUtf8Bytes(new{v=2,id=pcId,fingerprint,service=Config.Service,room=Config.Room,key=Config.PhoneKey,secret=Config.Secret,name=Environment.MachineName});return "abremote:"+Convert.ToBase64String(payload).TrimEnd('=').Replace('+','-').Replace('/','_');}
    public void ShowPairing(Form owner){using var generator=new QRCodeGenerator();using var data=generator.CreateQrCode(PairingCode(),QRCodeGenerator.ECCLevel.M);using var qr=new QRCode(data);var dialog=new Form{Text="Connect a device",Size=new(500,600),StartPosition=FormStartPosition.CenterParent};dialog.Controls.Add(new PictureBox{Dock=DockStyle.Fill,Image=qr.GetGraphic(6),SizeMode=PictureBoxSizeMode.Zoom});dialog.Controls.Add(new Label{Dock=DockStyle.Top,Height=95,Padding=new(18),Text="Phone: Add computer → Scan QR code.\nComputer: Add device → Connect a computer → paste the copied code.\nKeep this pairing code private: it grants file and action access to this PC."});var copy=new Button{Text="Copy pairing code (manual entry)",Dock=DockStyle.Bottom,Height=44};copy.Click+=(s,e)=>Clipboard.SetText(PairingCode());dialog.Controls.Add(copy);dialog.ShowDialog(owner);}
    public async Task Disconnect(){await configGate.WaitAsync();try{if(Config==null){Wire.AtomicJson(path+".disabled",true);return;}using var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(15)};using var request=new HttpRequestMessage(HttpMethod.Delete,Config.Service+"/rooms/"+Config.Room+"/delete?role=pc");request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",Config.PcKey);using var response=await client.SendAsync(request);if(!response.IsSuccessStatusCode&&response.StatusCode!=System.Net.HttpStatusCode.NotFound)throw new InvalidOperationException("Cannot revoke access while the service is unavailable. Try again when online.");Wire.AtomicJson(path+".disabled",true);Stop();Config=null;File.Delete(path);File.Delete(path+".enroll");File.Delete(path+".linked");Changed?.Invoke("Remote access disabled and phone access revoked.");}finally{configGate.Release();}}
    public void Stop(){IsConnected=false;if(helper!=null){try{if(!helper.HasExited)helper.Kill(true);}catch{}helper.Dispose();helper=null;}}
    public void Dispose()=>Stop();
}
public sealed record RemoteConfig(string Service,string Room,string PcKey,string PhoneKey,string Secret,string Folder);
