using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
namespace ActionBridge.Core;
public sealed record ComputerPeer(string Id,string Name,string Host,int Port,string Fingerprint,string Token,string? Code=null) {
 public string Recipient=>"computer:"+Id;
 public bool Internet=>Code!=null;
}
public sealed record ComputerEndpoint(string Id,string Name,string Host,int Port,string Fingerprint);
public interface IComputerTransport:IDisposable {
 Task<JsonElement> Call(string method,object? values=null,CancellationToken ct=default);
 int DownloadSize {get;}
}
public sealed class LanComputerTransport:IComputerTransport {
 readonly HttpClient client;readonly ComputerPeer peer;
 public int DownloadSize=>262144;
 public LanComputerTransport(ComputerPeer peer){ComputerRules.Local(peer.Host,peer.Port);this.peer=peer;client=new(new HttpClientHandler{AllowAutoRedirect=false,ServerCertificateCustomValidationCallback=(_,c,_,_)=>c!=null&&Convert.ToHexString(SHA256.HashData(c.RawData)).Equals(peer.Fingerprint,StringComparison.OrdinalIgnoreCase)}){Timeout=TimeSpan.FromSeconds(75)};}
 public async Task<JsonElement> Call(string method,object? values=null,CancellationToken ct=default){var v=JsonSerializer.SerializeToElement(values??new{},Wire.JsonCompact);string path;var verb=HttpMethod.Post;HttpContent? body=null;
  switch(method){
   case "hello":path="/hello";verb=HttpMethod.Get;break;
   case "pair":path="/pair";body=JsonContent(v);break;
   case "create":path="/jobs";body=JsonContent(v.GetProperty("job"));break;
   case "status":path="/jobs/"+Id(v,"jobId");verb=HttpMethod.Get;break;
   case "finish":case "cancel":path="/jobs/"+Id(v,"jobId")+"/"+method;break;
   case "append":path="/jobs/"+Id(v,"jobId")+"/content?offset="+v.GetProperty("offset").GetInt64();verb=HttpMethod.Put;body=new ByteArrayContent(Convert.FromBase64String(v.GetProperty("data").GetString()!));break;
   case "outbox":path="/outbox";verb=HttpMethod.Get;break;
   case "download":path="/outbox/"+Id(v,"itemId")+"?offset="+v.GetProperty("offset").GetInt64()+"&count="+v.GetProperty("count").GetInt32();verb=HttpMethod.Get;break;
   case "received":path="/outbox/"+Id(v,"itemId")+"/ack";break;
   default:throw new ArgumentException("Unsupported computer action.");
  }
  var host=peer.Host.Contains(':')?"["+peer.Host+"]":peer.Host;using var req=new HttpRequestMessage(verb,$"https://{host}:{peer.Port}/v1"+path){Content=body};if(peer.Token.Length>0)req.Headers.Authorization=new("Bearer",peer.Token);using var response=await client.SendAsync(req,HttpCompletionOption.ResponseHeadersRead,ct);await using var stream=await response.Content.ReadAsStreamAsync(ct);using var output=new MemoryStream();var buffer=new byte[65536];int n;while((n=await stream.ReadAsync(buffer,ct))>0){if(output.Length+n>1024*1024)throw new IOException("Computer response too large.");output.Write(buffer,0,n);}var json=JsonSerializer.Deserialize<JsonElement>(output.ToArray());if(!response.IsSuccessStatusCode)throw new IOException(json.TryGetProperty("error",out var error)?error.GetString():"Computer refused the request.");return json;
 }
 static string Id(JsonElement v,string field){var id=v.GetProperty(field).GetString()!;if(!Guid.TryParseExact(id,"D",out var parsed)||parsed.ToString()!=id)throw new ArgumentException("Invalid transfer ID.");return id;}
 static StringContent JsonContent(JsonElement v)=>new(v.GetRawText(),Encoding.UTF8,"application/json");
 public void Dispose()=>client.Dispose();
}
public static class ComputerRules {
 public static void Local(string host,int port){if(!IPAddress.TryParse(host,out var address)||!BridgeServer.Local(address)||port is <1 or >65535)throw new ArgumentException("Enter a local computer IP address and port.");}
 public static JsonElement Pairing(string code){if(!code.StartsWith("abremote:"))throw new ArgumentException("Paste the destination computer’s ActionBridge pairing code.");try{var value=code[9..].Replace('-','+').Replace('_','/');if(value.Length>8192)throw new ArgumentException();var j=JsonSerializer.Deserialize<JsonElement>(Convert.FromBase64String(value.PadRight((value.Length+3)/4*4,'=')));if(!Guid.TryParseExact(j.GetProperty("id").GetString(),"D",out _))throw new ArgumentException();var uri=new Uri(j.GetProperty("service").GetString()!);if(uri.Scheme!="https"||uri.UserInfo!=""||uri.AbsolutePath!="/"||uri.Query!=""||uri.Fragment!="")throw new ArgumentException();foreach(var field in new[]{"key","secret"}){var key=j.GetProperty(field).GetString()!;if(key.Length!=43||!key.All(c=>char.IsAsciiLetterOrDigit(c)||c is '-' or '_'))throw new ArgumentException();}var room=j.GetProperty("room").GetString()!;if(room.Length!=32||!room.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f'))throw new ArgumentException();return j;}catch(Exception e)when(e is FormatException or JsonException or KeyNotFoundException or InvalidOperationException or UriFormatException or ArgumentException){throw new ArgumentException("Invalid ActionBridge pairing code.");}}
}
public sealed class RemoteComputerTransport:IComputerTransport {
 readonly Process process;readonly SemaphoreSlim writer=new(1,1);readonly ConcurrentDictionary<string,TaskCompletionSource<JsonElement>> pending=new();readonly TaskCompletionSource ready=new(TaskCreationOptions.RunContinuationsAsynchronously);bool disposed;
 public int DownloadSize=>16384;
 public RemoteComputerTransport(ComputerPeer peer){var j=ComputerRules.Pairing(peer.Code!);var exe=Path.Combine(AppContext.BaseDirectory,OperatingSystem.IsWindows()?"ActionBridge.Remote.exe":"ActionBridge.Remote");process=new(){StartInfo=new(exe){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true},EnableRaisingEvents=true};process.OutputDataReceived+=(_,e)=>{if(e.Data==null)return;try{var v=JsonSerializer.Deserialize<JsonElement>(e.Data);if(v.TryGetProperty("requestId",out var id)&&pending.TryGetValue(id.GetString()!,out var reply)){if(v.TryGetProperty("error",out var error))reply.TrySetException(new IOException(error.GetString()));else reply.TrySetResult(v.GetProperty("result").Clone());}else if(v.TryGetProperty("state",out var state)){if(state.GetString()=="ready")ready.TrySetResult();else if(state.GetString() is "offline" or "error")Fail(v.TryGetProperty("detail",out var detail)?detail.GetString():null);}}catch{}};process.Exited+=(_,_)=>Fail();process.ErrorDataReceived+=(_,_)=>{};process.Start();process.BeginOutputReadLine();process.BeginErrorReadLine();process.StandardInput.WriteLine(JsonSerializer.Serialize(new{mode="client",service=j.GetProperty("service").GetString(),room=j.GetProperty("room").GetString(),pcKey=j.GetProperty("key").GetString(),secret=j.GetProperty("secret").GetString(),folder=Path.GetTempPath(),parent=Environment.ProcessId,jobs=true}));process.StandardInput.Flush();}
 void Fail(string? message=null){var e=new IOException(message??"Computer connection interrupted. Files remain queued for retry.");ready.TrySetException(e);foreach(var p in pending.Values)p.TrySetException(e);}
 public async Task<JsonElement> Call(string method,object? values=null,CancellationToken ct=default){await ready.Task.WaitAsync(TimeSpan.FromSeconds(45),ct);if(disposed||process.HasExited)throw new IOException("Computer connection is closed.");var id=Guid.NewGuid().ToString();var reply=new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);pending[id]=reply;try{var body=JsonSerializer.SerializeToElement(values??new{},Wire.JsonCompact);var request=new Dictionary<string,object?>();foreach(var p in body.EnumerateObject())request[p.Name]=p.Value;request["method"]=method;request["requestId"]=id;await writer.WaitAsync(ct);try{await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request,Wire.JsonCompact));await process.StandardInput.FlushAsync(ct);}finally{writer.Release();}return await reply.Task.WaitAsync(TimeSpan.FromSeconds(100),ct);}finally{pending.TryRemove(id,out _);}}
 public void Dispose(){if(disposed)return;disposed=true;Fail();try{process.StandardInput.Close();if(!process.HasExited)process.Kill(true);}catch{}process.Dispose();}
}
