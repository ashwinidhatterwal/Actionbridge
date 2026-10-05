using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using ActionBridge.Core;
using ActionBridge.Ubuntu;

return await Host.Run(args);
public sealed class Host: IAsyncDisposable {
 [DllImport("libc")]static extern uint umask(uint mode);
 [DllImport("libc")]static extern int flock(int fd,int operation);
 readonly UiPipe ui=new();readonly Commands commands=new();readonly SemaphoreSlim mutations=new(1,1);readonly CancellationTokenSource stop=new();
 string root="",received="",id="";TrustStore trust=null!;Transfers transfers=null!;Outbox outbox=null!;Computers computers=null!;LinuxActions actions=null!;BridgeServer? server;RemoteConnection? remote;FileStream? instance;bool autoEnroll=true;int retrySeconds=30;
 public static async Task<int> Run(string[] args){if(!OperatingSystem.IsLinux()){Console.Error.WriteLine("Ubuntu host requires Linux.");return 1;}umask(0x3f);await using var host=new Host();try{await host.Start(args);await host.ReadLoop();return 0;}catch(Exception e){host.ui.Event("fatal",new{message=e.Message});return 1;}}
 async Task Start(string[] args){
  var data=Environment.GetEnvironmentVariable("XDG_DATA_HOME");root=Path.Combine(Path.IsPathFullyQualified(data??"")?data!:Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".local","share"),"actionbridge");
  var rootOption=Array.IndexOf(args,"--data-dir");if(rootOption>=0&&rootOption+1<args.Length)root=Path.GetFullPath(args[rootOption+1]);autoEnroll=!args.Contains("--offline-test")&&!File.Exists(Path.Combine(root,"remote.disabled"));
  Directory.CreateDirectory(root);if(new DirectoryInfo(root).LinkTarget!=null)throw new InvalidOperationException("Private app folder must not be a symbolic link.");File.SetUnixFileMode(root,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
  try{instance=new FileStream(Path.Combine(root,"session.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.ReadWrite);}catch(IOException){throw new InvalidOperationException("ActionBridge is already running for this user. Open its desktop launcher.");}if(flock(instance.SafeFileHandle.DangerousGetHandle().ToInt32(),6)!=0)throw new InvalidOperationException("ActionBridge is already running for this user. Open its desktop launcher.");
  var downloadDir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads");try{var found=await commands.Run("/usr/bin/xdg-user-dir",new[]{"DOWNLOAD"},5);if(found.Exit==0&&Path.IsPathFullyQualified(found.Output.Trim()))downloadDir=found.Output.Trim();}catch{}
  if(rootOption>=0)downloadDir=Path.Combine(root,"downloads");received=Path.Combine(downloadDir,"ActionBridge");Directory.CreateDirectory(received);
  var identityPath=Path.Combine(root,"identity.pfx");if(!File.Exists(identityPath)){using var fs=new FileStream(identityPath,FileMode.CreateNew,FileAccess.Write,FileShare.None);fs.Write(ServerIdentity.CreatePfx());fs.Flush(true);}File.SetUnixFileMode(identityPath,UnixFileMode.UserRead|UnixFileMode.UserWrite);
  var cert=ServerIdentity.Load(File.ReadAllBytes(identityPath));var idPath=Path.Combine(root,"pc-id.json");id=File.Exists(idPath)?JsonSerializer.Deserialize<string>(File.ReadAllText(idPath))!:Guid.NewGuid().ToString();if(!Guid.TryParseExact(id,"D",out _))throw new InvalidDataException("Computer identity is damaged. Restore its private app-data backup.");Wire.AtomicJson(idPath,id);
  trust=new(root);actions=new(commands,ui.Request);transfers=new(Path.Combine(root,"jobs"),received,actions,"Submitted to the Ubuntu CUPS queue; physical printing is not confirmed.");outbox=new(Path.Combine(root,"outbox"));
  computers=new(root,id,Environment.MachineName,outbox,transfers);computers.Changed+=()=>ui.Event("changed",new{});computers.Start();trust.Changed+=()=>ui.Event("changed",new{});
  remote=new(root,received,id,Convert.ToHexString(SHA256.HashData(cert.RawData)).ToLowerInvariant(),transfers,actions,outbox);remote.Changed+=()=>ui.Event("remote",new{message=remote.Status});
  server=new(id,Environment.MachineName,cert,trust,transfers,actions,Approve,()=>remote.Config==null||!remote.Enabled?null:remote.PairingCode(),outbox);
  var port=Wire.ApiPort;var portOption=Array.IndexOf(args,"--port");if(portOption>=0)port=int.Parse(args[portOption+1]);await server.StartAsync(port,!args.Contains("--no-discovery"));
  transfers.Changed+=job=>{ui.Event("changed",new{});if(job.State is "completed" or "submitted" or "failed" or "uncertain")ui.Event("notification",new{message=job.State=="completed"?"Phone action completed":job.Message});};outbox.Changed+=()=>ui.Event("changed",new{});
  ui.Event("ready",new{name=Environment.MachineName,id,received,version="0.7.0",port});if(autoEnroll)_=Enroll();_=RemoteLoop();
 }
 async Task<bool> Approve(PairRequest request,string ip){try{var result=await ui.Request("approval",new{name=request.Name,ip});return result.GetProperty("allowed").GetBoolean();}catch{return false;}}
 async Task Enroll(){try{await remote!.Configure();retrySeconds=30;}catch{retrySeconds=Math.Min(retrySeconds*2,300);}}
 async Task RemoteLoop(){try{while(!stop.IsCancellationRequested){await Task.Delay(TimeSpan.FromSeconds(retrySeconds),stop.Token);if(remote?.Config!=null)remote.Start();else if(autoEnroll)await Enroll();}}catch(OperationCanceledException){}}
 async Task ReadLoop(){while(!stop.IsCancellationRequested){var line=await Console.In.ReadLineAsync();if(line==null)break;if(line.Length>4*1024*1024)continue;try{using var doc=JsonDocument.Parse(line);var message=doc.RootElement.Clone();if(ui.Reply(message))continue;if(message.GetProperty("method").GetString()=="quit")break;_=Respond(message);}catch{}}ui.Close();}
 async Task Respond(JsonElement message){var requestId=message.GetProperty("requestId").GetString();try{var method=message.GetProperty("method").GetString();object result;
  if(method=="snapshot")result=Snapshot();else{await mutations.WaitAsync(stop.Token);try{result=await Command(method!,message);}finally{mutations.Release();}}
  ui.Send(new{requestId,result});
 }catch(Exception e){ui.Send(new{requestId,error=e is ArgumentException or InvalidOperationException or IOException or KeyNotFoundException?e.Message:"Computer could not complete this request."});}}
 object Snapshot()=>new{phones=trust.List().Select(p=>new{id=p.ClientId,name=p.Name}),computers=computers.Snapshot(),remoteReady=remote?.Config!=null&&remote.Enabled,remoteLinked=remote?.HasLinkedDevice==true,remoteRecipient=remote?.Config==null?null:"remote:"+remote.Config.Room,remoteStatus=remote?.Config==null&&!autoEnroll?"Internet access is off · Connect phone to enable":remote?.Status,jobs=transfers.History().Select(j=>new{request=new{id=j.Request.Id,name=j.Request.Name,action=j.Request.Action,size=j.Request.Size},j.ClientId,j.Offset,j.State,message=j.Message is {Length:>1000}?j.Message[..1000]:j.Message,j.Updated}),outgoing=outbox.History().Select(o=>new{o.Id,o.Name,o.Kind,o.Size,o.State,o.Updated,o.Offset}),received};
 async Task<object> Command(string method,JsonElement m){switch(method){
  case "pairing":await remote!.Configure(true);autoEnroll=true;return new{code=remote.PairingCode()};
  case "revokeRemote":await remote!.Disconnect();autoEnroll=false;Wire.AtomicJson(Path.Combine(root,"remote.disabled"),true);return new{done=true};
  case "revoke":trust.Revoke(m.GetProperty("id").GetString()!);return new{done=true};
  case "discoverComputers":return await computers.Discover(stop.Token);
  case "addComputer":if(m.TryGetProperty("code",out var code))computers.AddRemote(code.GetString()!);else await computers.AddLocal(m.GetProperty("host").GetString()!,m.TryGetProperty("port",out var peerPort)?peerPort.GetInt32():Wire.ApiPort,stop.Token);return new{done=true};
  case "removeComputer":computers.Remove(m.GetProperty("id").GetString()!);return new{done=true};
  case "sendFiles":var recipient=Recipient(m);var paths=m.GetProperty("paths").Deserialize<string[]>()??[];if(paths.Length is <1 or >20)throw new ArgumentException("Choose 1–20 files.");foreach(var path in paths){if(!Path.IsPathFullyQualified(path)||!File.Exists(path))throw new ArgumentException("Choose files rather than folders.");await outbox.Stage(recipient,path);}return new{queued=paths.Length};
  case "sendText":await outbox.Stage(Recipient(m),null,m.GetProperty("text").GetString(),m.GetProperty("kind").GetString()!);return new{queued=1};
  case "cancelOutgoing":outbox.Cancel(m.GetProperty("id").GetString()!);return new{done=true};
  case "cancelJob":var j=transfers.History().FirstOrDefault(x=>x.Request.Id==m.GetProperty("id").GetString())??throw new KeyNotFoundException("Job unavailable.");return await transfers.Cancel(j.Request.Id,j.ClientId);
  case "openFolder":await actions.Open(received);return new{done=true};
  case "openJob":var job=transfers.History().FirstOrDefault(x=>x.Request.Id==m.GetProperty("id").GetString())??throw new KeyNotFoundException("File unavailable.");var file=transfers.Destination(job);if(!File.Exists(file)||!LinuxActions.AllowedOpen(file))throw new InvalidOperationException("Use the received folder to inspect this file type.");await actions.Open(file);return new{done=true};
  case "printers":return await actions.ListPrinters();
  default:throw new ArgumentException("Unsupported desktop action.");
 }}
 string Recipient(JsonElement m){var recipient=m.GetProperty("recipient").GetString()!;if(computers.Contains(recipient)||trust.List().Any(p=>p.ClientId==recipient)||remote?.Config!=null&&recipient=="remote:"+remote.Config.Room)return recipient;throw new ArgumentException("Choose a paired phone or computer.");}
 public async ValueTask DisposeAsync(){stop.Cancel();computers?.Dispose();ui.Close();remote?.Dispose();if(server!=null)await server.DisposeAsync();instance?.Dispose();stop.Dispose();}
}
