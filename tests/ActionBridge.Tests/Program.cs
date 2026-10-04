using ActionBridge.Core;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
var root=Path.Combine(Path.GetTempPath(),"bridge-tests-"+Guid.NewGuid());Directory.CreateDirectory(root);
var passes=0;
void Assert(bool condition,string label){if(!condition)throw new Exception("FAIL: "+label);Console.WriteLine("PASS: "+label);passes++;}
async Task Throws<T>(Func<Task> action,string label)where T:Exception {try{await action();throw new Exception("Expected "+typeof(T).Name);}catch(T){Assert(true,label);}}
var sink=new FakeSink();var trust=new TrustStore(root);var phone=new PairRequest(Guid.NewGuid().ToString(),"Test Phone",new string('a',64));trust.Approve(phone);
Assert(trust.Authenticate(phone.Token)==phone.ClientId,"approved token authenticates");Assert(trust.Authenticate(new string('b',64))==null,"unknown phone denied");
Assert(!File.ReadAllText(Path.Combine(root,"phones.json")).Contains(phone.Token),"raw token never persisted on PC");
trust=new TrustStore(root);Assert(trust.AlreadyApproved(phone),"trust survives restart");trust.Revoke(phone.ClientId);Assert(trust.Authenticate(phone.Token)==null,"revocation immediate");trust.Approve(phone);
Assert(Wire.SafeName("../../CON.txt")=="_CON.txt","path traversal and reserved filename sanitized");Assert(Wire.SafeName(@"C:\evil\file?.pdf")=="file_.pdf","Windows separators sanitized");
Assert(BridgeServer.Local(IPAddress.Parse("192.168.1.10"))&&!BridgeServer.Local(IPAddress.Parse("8.8.8.8")),"public addresses rejected");
var jobs=new Transfers(Path.Combine(root,"jobs"),Path.Combine(root,"files"),sink);
var bytes=Encoding.UTF8.GetBytes("invoice contents\n");var sha=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
JobRequest Request(string action="save")=>new(Guid.NewGuid().ToString(),"invoice.pdf",bytes.Length,sha,action);
var req=Request();var job=await jobs.Create(req,phone.ClientId);Assert(job.Offset==0,"new transfer starts at zero");
Assert(ReferenceEquals(job,await jobs.Create(req,phone.ClientId)),"duplicate create is idempotent");
await Throws<ArgumentException>(()=>jobs.Create(req with{Name="different.pdf"},phone.ClientId),"ID cannot change payload");
await Throws<KeyNotFoundException>(()=>Task.FromResult(jobs.Get(req.Id,"other-client")),"transfer ownership enforced");
await Throws<InvalidOperationException>(()=>jobs.Finish(req.Id,phone.ClientId),"incomplete file cannot execute");
await Throws<InvalidOperationException>(()=>jobs.Append(req.Id,phone.ClientId,2,new MemoryStream(bytes),bytes.Length,CancellationToken.None),"wrong offset rejected");
await Throws<EndOfStreamException>(()=>jobs.Append(req.Id,phone.ClientId,0,new MemoryStream(bytes[..3]),bytes.Length,CancellationToken.None),"interrupted upload rejected");Assert(job.Offset==0,"interrupted upload does not advance checkpoint");
await jobs.Append(req.Id,phone.ClientId,0,new MemoryStream(bytes[..5]),5,CancellationToken.None);
// Simulate a process crash with extra bytes written after the last committed checkpoint.
using(var fs=new FileStream(Path.Combine(root,"jobs",req.Id+".part"),FileMode.Append))fs.Write(bytes[..3]);
jobs=new Transfers(Path.Combine(root,"jobs"),Path.Combine(root,"files"),sink);job=jobs.Get(req.Id,phone.ClientId);
Assert(job.Offset==5&&new FileInfo(Path.Combine(root,"jobs",req.Id+".part")).Length==5,"recovery truncates uncommitted bytes");
await jobs.Append(req.Id,phone.ClientId,5,new MemoryStream(bytes[5..]),bytes.Length-5,CancellationToken.None);
await jobs.Finish(req.Id,phone.ClientId);await jobs.Finish(req.Id,phone.ClientId);
for(var i=0;i<100&&job.State is "queued" or "running";i++)await Task.Delay(10);
Assert(File.ReadAllBytes(jobs.Destination(job)).SequenceEqual(bytes),"received file byte-identical");Assert(sink.Count==1,"finish retry executes only once");
var corrupt=Request();await jobs.Create(corrupt,phone.ClientId);await jobs.Append(corrupt.Id,phone.ClientId,0,new MemoryStream(new byte[bytes.Length]),bytes.Length,CancellationToken.None);
await Throws<InvalidDataException>(()=>jobs.Finish(corrupt.Id,phone.ClientId),"checksum mismatch blocks action");
await Throws<ArgumentException>(()=>jobs.Create(Request() with{Size=Wire.MaxFile+1},phone.ClientId),"oversized file rejected");
await Throws<ArgumentException>(()=>jobs.Create(Request("print") with{Name="script.exe"},phone.ClientId),"unsupported print format rejected");
await Throws<ArgumentException>(()=>jobs.Create(Request("url") with{Size=0,Text="file:///C:/Windows"},phone.ClientId),"non-web URL rejected");
var print=Request("print");var pj=await jobs.Create(print,phone.ClientId);await jobs.Append(print.Id,phone.ClientId,0,new MemoryStream(bytes),bytes.Length,CancellationToken.None);await jobs.Finish(print.Id,phone.ClientId);for(var i=0;i<100&&pj.State is "queued" or "running";i++)await Task.Delay(10);Assert(pj.State=="submitted","printing reports submitted, not printed");
var recovery=Request("print");var rj=new Job{Request=recovery,ClientId=phone.ClientId,State="running"};Wire.AtomicJson(Path.Combine(root,"jobs",recovery.Id+".json"),rj);
var restored=new Transfers(Path.Combine(root,"jobs"),Path.Combine(root,"files"),sink);var before=sink.Count;await restored.Finish(recovery.Id,phone.ClientId);Assert(restored.Get(recovery.Id,phone.ClientId).State=="uncertain"&&sink.Count==before,"in-flight print is never replayed after restart");
// Exercise the production import path after the generating RSA is disposed.
var pfx=ServerIdentity.CreatePfx();using var cert=ServerIdentity.Load(pfx);
using(var key=cert.GetRSAPrivateKey()) {
    var signature=key!.SignData(bytes,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
    using var publicKey=cert.GetRSAPublicKey();
    Assert(publicKey!.VerifyData(bytes,signature,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1),"imported identity key works after generator disposal");
}
using(var reloaded=ServerIdentity.Load(pfx)) Assert(reloaded.RawData.SequenceEqual(cert.RawData)&&reloaded.HasPrivateKey,"identity reload preserves phone certificate pin");
using var handler=new HttpClientHandler{ServerCertificateCustomValidationCallback=(a,b,c,d)=>b!=null&&SHA256.HashData(b.RawData).SequenceEqual(SHA256.HashData(cert.RawData))};using var client=new HttpClient(handler){BaseAddress=new Uri("https://127.0.0.1:45839")};
await using var server=new BridgeServer(Guid.NewGuid().ToString(),"Fixture PC",cert,trust,jobs,sink,(r,ip)=>Task.FromResult(true));await server.StartAsync(45839,false);
Assert((await client.GetAsync("/v1/printers")).StatusCode==HttpStatusCode.Unauthorized,"API blocks unauthenticated printer access");
var apiPhone=new PairRequest(Guid.NewGuid().ToString(),"API Phone",new string('c',64));Assert((await client.PostAsJsonAsync("/v1/pair",apiPhone)).IsSuccessStatusCode,"PC approval endpoint accepts approved phone");
client.DefaultRequestHeaders.Authorization=new("Bearer",apiPhone.Token);Assert((await client.GetAsync("/v1/printers")).IsSuccessStatusCode,"authenticated printer listing");
var ar=Request();var create=await client.PostAsJsonAsync("/v1/jobs",ar);Assert(create.IsSuccessStatusCode,"HTTPS job creation");
var content=new ByteArrayContent(bytes);var upload=await client.PutAsync($"/v1/jobs/{ar.Id}/content?offset=0",content);Assert(upload.IsSuccessStatusCode,"HTTPS chunk upload");Assert((await client.PostAsJsonAsync($"/v1/jobs/{ar.Id}/finish",new{})).IsSuccessStatusCode,"HTTPS finish action");
trust.Revoke(apiPhone.ClientId);Assert((await client.GetAsync("/v1/printers")).StatusCode==HttpStatusCode.Unauthorized,"API revocation enforced on next request");
// Exercise duplicate finish while a slow printer action is still running.
var slow=new SlowSink();var raceJobs=new Transfers(Path.Combine(root,"race"),Path.Combine(root,"racefiles"),slow);
var raceReq=Request("print");await raceJobs.Create(raceReq,phone.ClientId);await raceJobs.Append(raceReq.Id,phone.ClientId,0,new MemoryStream(bytes),bytes.Length,CancellationToken.None);
await Task.WhenAll(Enumerable.Range(0,12).Select(_=>raceJobs.Finish(raceReq.Id,phone.ClientId)));
for(var i=0;i<100&&slow.Count==0;i++)await Task.Delay(10);Assert(slow.Count==1,"concurrent finish retries initiate one print");slow.Done.SetResult();
for(var i=0;i<50&&raceJobs.Get(raceReq.Id,phone.ClientId).State=="running";i++)await Task.Delay(10);
Assert(raceJobs.Get(raceReq.Id,phone.ClientId).State=="submitted","slow action completes after concurrent retries");
var emptyReq=new JobRequest(Guid.NewGuid().ToString(),"empty.txt",0,Convert.ToHexString(SHA256.HashData(Array.Empty<byte>())).ToLowerInvariant(),"save");
var empty=await jobs.Create(emptyReq,phone.ClientId);await jobs.Finish(emptyReq.Id,phone.ClientId);Assert(new FileInfo(jobs.Destination(empty)).Length==0,"zero-byte file transfer succeeds");
await Throws<ArgumentException>(()=>jobs.Create(Request() with{Name=null!},phone.ClientId),"missing JSON fields rejected safely");
var cancelledReq=Request();await jobs.Create(cancelledReq,phone.ClientId);await jobs.Append(cancelledReq.Id,phone.ClientId,0,new MemoryStream(bytes[..5]),5,CancellationToken.None);
await jobs.Cancel(cancelledReq.Id,phone.ClientId);var countBeforeCancel=sink.Count;await jobs.Finish(cancelledReq.Id,phone.ClientId);
Assert(jobs.Get(cancelledReq.Id,phone.ClientId).State=="cancelled"&&sink.Count==countBeforeCancel,"cancelled partial cannot execute on retry");
Assert(!File.Exists(Path.Combine(root,"jobs",cancelledReq.Id+".part")),"cancellation removes PC partial file");
await Throws<ArgumentException>(()=>jobs.Create(Request() with{Id="AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA"},phone.ClientId),"noncanonical UUID rejected to prevent Windows path aliasing");
await Throws<ArgumentException>(()=>jobs.Create(Request("print") with{Duplex="invalid"},phone.ClientId),"invalid duplex rejected");
await Throws<ArgumentException>(()=>jobs.Create(Request("print") with{PageFrom=5,PageTo=2},phone.ClientId),"reversed PDF range rejected");
await Throws<ArgumentException>(()=>jobs.Create(Request("print") with{Name="photo.jpg",PageFrom=1,PageTo=2},phone.ClientId),"image page range rejected");
var rangeRequest=Request("print") with{PageFrom=2,PageTo=5,Duplex="long",Collate=false};
Wire.Validate(rangeRequest);Assert(true,"valid PDF range and duplex accepted");
// Remote data-channel requests use exactly the same durable journal and sink.
var remoteSink=new FakeSink();var remoteTransfers=new Transfers(Path.Combine(root,"remote-jobs"),Path.Combine(root,"remote-files"),remoteSink);
var remote=new RemoteJobs(remoteTransfers,remoteSink,"paired-room");
JsonElement Message(object value)=>JsonSerializer.SerializeToElement(value,Wire.Json);
var remotePrint=Request("print") with{Printer="Fixture Printer",Copies=3,PageFrom=2,PageTo=5,Duplex="long",Collate=false};
await remote.Dispatch(Message(new{method="create",job=remotePrint}));
await remote.Dispatch(Message(new{method="append",jobId=remotePrint.Id,offset=0,data=Convert.ToBase64String(bytes)}));
await Task.WhenAll(Enumerable.Range(0,8).Select(_=>remote.Dispatch(Message(new{method="finish",jobId=remotePrint.Id}))));
for(var i=0;i<100&&remoteTransfers.Get(remotePrint.Id,"remote:paired-room").State is "queued" or "running";i++)await Task.Delay(10);
Assert(remoteSink.Count==1,"remote finish retries print only once");
Assert(remoteSink.Last==remotePrint,"remote print retains all local printer options");
Assert(remoteTransfers.History().Single().State=="submitted","remote print appears in shared activity journal");
Assert(((IReadOnlyList<PrintChoice>)await remote.Dispatch(Message(new{method="printers"}))).Count==1,"remote printer discovery uses the local printer sink");
var otherRemote=new RemoteJobs(remoteTransfers,remoteSink,"different-room");
await Throws<KeyNotFoundException>(()=>otherRemote.Dispatch(Message(new{method="status",jobId=remotePrint.Id})),"remote room cannot inspect another room's job");
await Throws<ArgumentException>(()=>remote.Dispatch(Message(new{method="shell",command="anything"})),"remote dispatcher has no arbitrary command operation");
foreach(var action in new[]{"copy","url"}){
 var textReq=new JobRequest(Guid.NewGuid().ToString(),action=="copy"?"Text":"Web link",0,"",action,Text:action=="copy"?"hello":"https://example.com");
 await remote.Dispatch(Message(new{method="create",job=textReq}));await remote.Dispatch(Message(new{method="finish",jobId=textReq.Id}));
 for(var i=0;i<100&&remoteTransfers.Get(textReq.Id,"remote:paired-room").State is "queued" or "running";i++)await Task.Delay(10);
 Assert(remoteTransfers.Get(textReq.Id,"remote:paired-room").State=="completed","remote "+action+" completes through shared action sink");
}
// Two-way transfers are PC-owned, addressed, durable, and bounded.
var outgoing=new Outbox(Path.Combine(root,"outbox"));var sourceFile=Path.Combine(root,"send.txt");await File.WriteAllBytesAsync(sourceFile,bytes);
var sentFile=await outgoing.Stage(phone.ClientId,sourceFile);
await File.WriteAllTextAsync(sourceFile,"changed original");
Assert(outgoing.List(phone.ClientId).Single().Sha256==Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),"outbox snapshots source file before sending");
Assert(outgoing.List("other-phone").Length==0,"another phone cannot list outgoing files");
await Throws<KeyNotFoundException>(()=>Task.Run(()=>outgoing.Read(sentFile.Id,"other-phone",0,16)),"another phone cannot read outgoing file");
await Throws<ArgumentException>(()=>Task.Run(()=>outgoing.Read(sentFile.Id,phone.ClientId,0,262145)),"outgoing download chunks bounded");
var block=JsonSerializer.SerializeToElement(outgoing.Read(sentFile.Id,phone.ClientId,0,16384),Wire.Json);
Assert(Convert.FromBase64String(block.GetProperty("data").GetString()!).SequenceEqual(bytes),"outgoing file bytes match staged snapshot");
var reloadedOutbox=new Outbox(Path.Combine(root,"outbox"));Assert(reloadedOutbox.List(phone.ClientId).Single().Id==sentFile.Id,"outbox survives PC restart");
reloadedOutbox.Acknowledge(sentFile.Id,phone.ClientId);reloadedOutbox.Acknowledge(sentFile.Id,phone.ClientId);
Assert(reloadedOutbox.List(phone.ClientId).Length==0&&!File.Exists(Path.Combine(root,"outbox",sentFile.Id+".payload")),"received acknowledgement is idempotent and removes staged bytes");
var cancelledSend=await reloadedOutbox.Stage(phone.ClientId,sourceFile);reloadedOutbox.Cancel(cancelledSend.Id);
await Throws<ArgumentException>(()=>Task.Run(()=>reloadedOutbox.Read(cancelledSend.Id,phone.ClientId,0,16)),"cancelled outgoing file cannot be downloaded");
await Throws<ArgumentException>(()=>reloadedOutbox.Stage(phone.ClientId,null,"javascript:alert(1)","link"),"outgoing link cannot execute script");
await Throws<ArgumentException>(()=>reloadedOutbox.Stage(phone.ClientId,null,new string('क',3000),"text"),"outgoing Unicode text byte bound enforced");
var remoteOutgoing=await reloadedOutbox.Stage("remote:paired-room",sourceFile);var withOutbox=new RemoteJobs(remoteTransfers,remoteSink,"paired-room",reloadedOutbox);
Assert(((Outgoing[])await withOutbox.Dispatch(Message(new{method="outbox"}))).Single().Id==remoteOutgoing.Id,"remote phone sees addressed PC outbox");
var remoteBlock=JsonSerializer.SerializeToElement(await withOutbox.Dispatch(Message(new{method="download",itemId=remoteOutgoing.Id,offset=0,count=16384})),Wire.Json);
var expectedRemoteBytes=await File.ReadAllBytesAsync(sourceFile);Assert(Convert.FromBase64String(remoteBlock.GetProperty("data").GetString()!).SequenceEqual(expectedRemoteBytes),"remote dispatcher returns outgoing snapshot bytes");
await withOutbox.Dispatch(Message(new{method="received",itemId=remoteOutgoing.Id}));Assert(reloadedOutbox.History().Single(x=>x.Id==remoteOutgoing.Id).State=="delivered","remote receive completes PC sent history");
// Live HTTPS reverse endpoints enforce the same trust boundary.
var apiOutgoing=new Outbox(Path.Combine(root,"api-outbox"));var authPhone=new PairRequest(Guid.NewGuid().ToString(),"Receive phone",new string('d',64));trust.Approve(authPhone);
await using(var reverseServer=new BridgeServer(Guid.NewGuid().ToString(),"Send PC",cert,trust,jobs,sink,(r,ip)=>Task.FromResult(true),outbox:apiOutgoing)){
 await reverseServer.StartAsync(45838,false);using var reverseClient=new HttpClient(handler,false){BaseAddress=new Uri("https://127.0.0.1:45838")};
 Assert((await reverseClient.GetAsync("/v1/outbox")).StatusCode==HttpStatusCode.Unauthorized,"HTTPS outgoing listing requires pairing");reverseClient.DefaultRequestHeaders.Authorization=new("Bearer",authPhone.Token);
 var packet=await apiOutgoing.Stage(authPhone.ClientId,sourceFile);Assert((await reverseClient.GetStringAsync("/v1/outbox")).Contains(packet.Id),"HTTPS phone lists addressed outgoing file");
 Assert((await reverseClient.GetAsync($"/v1/outbox/{packet.Id}?offset=0&count=16384")).IsSuccessStatusCode,"HTTPS phone downloads outgoing content");
 Assert((await reverseClient.PostAsJsonAsync($"/v1/outbox/{packet.Id}/ack",new{})).IsSuccessStatusCode,"HTTPS receive acknowledgement completes outgoing file");
}
// Audit: raster limits include height, and approval notifications follow commit.
var tall=PrintGeometry.PdfRaster(100,100000);Assert(tall.Height<=5000&&tall.Width<=5000,"very tall PDF raster is bounded in both dimensions");
var normal=PrintGeometry.PdfRaster(793.7,1122.5);Assert(normal.Dpi<=301&&normal.Width<5000&&normal.Height<5000,"ordinary PDF keeps approximately 300 DPI");
await Throws<ArgumentException>(()=>Task.Run(()=>PrintGeometry.PdfRaster(double.NaN,100)),"invalid PDF geometry rejected");
var notificationTrust=new TrustStore(Path.Combine(root,"notify-trust"));var observed=false;var notifyPhone=new PairRequest(Guid.NewGuid().ToString(),"Phone",new string('e',64));
notificationTrust.Changed+=()=>{observed=notificationTrust.Authenticate(notifyPhone.Token)==notifyPhone.ClientId;throw new Exception("UI unavailable");};notificationTrust.Approve(notifyPhone);Assert(observed&&notificationTrust.Authenticate(notifyPhone.Token)==notifyPhone.ClientId,"phone UI notification follows durable approval and cannot interrupt it");
await Throws<ArgumentException>(()=>Task.Run(()=>notificationTrust.Approve(notifyPhone with{Name=null!})),"missing pairing name rejected without server error");
Console.WriteLine($"\n{passes} checks passed.");
// Test directory is temporary; no user content.
Directory.Delete(root,true);
sealed class FakeSink:IActionSink{public int Count;public JobRequest? Last;public Task ExecuteAsync(JobRequest r,string? file){Last=r;Count++;return Task.CompletedTask;}public IReadOnlyList<PrintChoice> Printers()=>[new("Fixture Printer",["A4"],true)];}

sealed class SlowSink:IActionSink {public int Count;public TaskCompletionSource Done=new(TaskCreationOptions.RunContinuationsAsynchronously);public Task ExecuteAsync(JobRequest r,string? f){Interlocked.Increment(ref Count);return Done.Task;}public IReadOnlyList<PrintChoice> Printers()=>Array.Empty<PrintChoice>();}
