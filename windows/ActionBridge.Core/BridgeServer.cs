using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
namespace ActionBridge.Core;
public sealed class BridgeServer : IAsyncDisposable {
    readonly TrustStore trust; readonly Transfers transfers; readonly IActionSink sink;
    readonly Func<PairRequest,string,Task<bool>> approval;readonly Func<string?>? remotePairing;
    readonly X509Certificate2 certificate; readonly string id,name;
    readonly Outbox? outbox;
    readonly SemaphoreSlim pairGate=new(1,1);
    readonly System.Collections.Concurrent.ConcurrentDictionary<string,DateTime> pairTimes = new();
    WebApplication? app; UdpClient? udp; readonly CancellationTokenSource stop=new();
    public string Fingerprint => Convert.ToHexString(SHA256.HashData(certificate.RawData)).ToLowerInvariant();
    public BridgeServer(string id,string name,X509Certificate2 certificate,TrustStore trust,Transfers transfers,IActionSink sink,Func<PairRequest,string,Task<bool>> approval,Func<string?>? remotePairing=null,Outbox? outbox=null) {
        this.id=id;this.name=name;this.certificate=certificate;this.trust=trust;this.transfers=transfers;this.sink=sink;this.approval=approval;this.remotePairing=remotePairing;this.outbox=outbox;
    }
    public async Task StartAsync(int port=Wire.ApiPort,bool discover=true) {
        var b=WebApplication.CreateBuilder(); b.Logging.ClearProviders();
        b.WebHost.ConfigureKestrel(k=> { k.Limits.MaxRequestBodySize=Wire.ChunkSize; k.Limits.MaxConcurrentConnections=24; k.Limits.RequestHeadersTimeout=TimeSpan.FromSeconds(10); k.ListenAnyIP(port,o=>o.UseHttps(certificate)); });
        app=b.Build();
        app.Use(async (context,next)=> {
            if(!Local(context.Connection.RemoteIpAddress)) { context.Response.StatusCode=403; return; }
            try { await next(); }
            catch(Exception ex) {
                if(context.Response.HasStarted) { context.Abort(); return; }
                context.Response.StatusCode=ex switch { KeyNotFoundException=>404, ArgumentException=>400, InvalidOperationException=>409, InvalidDataException=>422, IOException=>507, _=>500 };
                await context.Response.WriteAsJsonAsync(new {error=ex is ArgumentException or InvalidOperationException or IOException or KeyNotFoundException ? ex.Message : "PC could not complete this request."});
            }
        });
        app.MapGet("/v1/hello",()=>new {version=1,id,name,fingerprint=Fingerprint,maxFile=Wire.MaxFile,chunkSize=Wire.ChunkSize});
        app.MapPost("/v1/pair",async (PairRequest request,HttpContext c)=> {
            if(!Guid.TryParse(request.ClientId,out _) || string.IsNullOrEmpty(request.Name) || request.Name.Length>80 || string.IsNullOrEmpty(request.Token) || request.Token.Length!=64 || !request.Token.All(Uri.IsHexDigit)) return Results.BadRequest(new{error="Invalid phone identity."});
            if(trust.AlreadyApproved(request)) return Results.Ok(new{approved=true});
            var ip=c.Connection.RemoteIpAddress!.ToString();
            if(pairTimes.TryGetValue(ip,out var last) && DateTime.UtcNow-last<TimeSpan.FromSeconds(10)) return Results.Json(new{error="Please wait before requesting approval again."},statusCode:429);
            if(!await pairGate.WaitAsync(0)) return Results.Json(new{error="Another phone is awaiting approval."},statusCode:429);
            try {
                // Bound unauthenticated state even on a hostile local network.
                foreach(var entry in pairTimes.Where(x=>DateTime.UtcNow-x.Value>TimeSpan.FromMinutes(1))) pairTimes.TryRemove(entry.Key,out _);
                pairTimes[ip]=DateTime.UtcNow;
                var accepted=await approval(request,ip).WaitAsync(TimeSpan.FromSeconds(60));
                if(!accepted) return Results.Json(new{error="Connection declined on PC."},statusCode:403);
                trust.Approve(request); return Results.Ok(new{approved=true});
            } catch(TimeoutException) { return Results.Json(new{error="PC approval expired. Try again."},statusCode:408); }
            finally {pairGate.Release();}
        });
        var api=app.MapGroup("/v1");
        api.AddEndpointFilter(async (context,next)=> {
            var auth=context.HttpContext.Request.Headers.Authorization.ToString();
            var client=auth.StartsWith("Bearer ",StringComparison.Ordinal) ? trust.Authenticate(auth[7..]) : null;
            if(client==null) return Results.Json(new{error="Approve this phone on the PC first."},statusCode:401);
            context.HttpContext.Items["client"]=client;return await next(context);
        });
        api.MapGet("/status",()=>new {ready=true,id});
        api.MapGet("/connection",()=>new {code=remotePairing?.Invoke()});
        api.MapGet("/outbox",(HttpContext c)=>outbox?.List((string)c.Items["client"]!)??[]);
        api.MapGet("/outbox/{itemId}",(string itemId,long offset,int count,HttpContext c)=>outbox?.Read(itemId,(string)c.Items["client"]!,offset,count)??throw new InvalidOperationException("Update the PC app."));
        api.MapPost("/outbox/{itemId}/ack",(string itemId,HttpContext c)=>outbox?.Acknowledge(itemId,(string)c.Items["client"]!)??throw new InvalidOperationException("Update the PC app."));
        api.MapGet("/printers",()=>sink.Printers());
        api.MapPost("/jobs",async(JobRequest r,HttpContext c)=>Results.Json(await transfers.Create(r,(string)c.Items["client"]!)));
        api.MapGet("/jobs/{jobId}",(string jobId,HttpContext c)=>Results.Json(transfers.Get(jobId,(string)c.Items["client"]!)));
        api.MapPut("/jobs/{jobId}/content",async(string jobId,long offset,HttpContext c)=>Results.Json(await transfers.Append(jobId,(string)c.Items["client"]!,offset,c.Request.Body,c.Request.ContentLength??-1,c.RequestAborted)));
        api.MapPost("/jobs/{jobId}/cancel",async(string jobId,HttpContext c)=>Results.Json(await transfers.Cancel(jobId,(string)c.Items["client"]!)));
        api.MapPost("/jobs/{jobId}/finish",async(string jobId,HttpContext c)=>Results.Json(await transfers.Finish(jobId,(string)c.Items["client"]!)));
        await app.StartAsync(); if(discover) StartDiscovery(port);
    }
    public static bool Local(IPAddress? address) {
        if(address==null) return false; if(address.IsIPv4MappedToIPv6) address=address.MapToIPv4();
        if(IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal) return true;
        var x=address.GetAddressBytes();
        return x.Length==4 ? x[0]==10 || x[0]==192&&x[1]==168 || x[0]==172&&x[1]>=16&&x[1]<=31 || x[0]==169&&x[1]==254 : (x[0]&0xfe)==0xfc;
    }
    void StartDiscovery(int port) {
        udp=new UdpClient(AddressFamily.InterNetwork); udp.ExclusiveAddressUse=false; udp.Client.SetSocketOption(SocketOptionLevel.Socket,SocketOptionName.ReuseAddress,true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any,Wire.DiscoveryPort));
        // Interface enumeration can be blocked in restricted Linux sessions. Keep
        // unicast/broadcast discovery and HTTPS working if multicast joins fail.
        try {
            foreach(var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==OperationalStatus.Up)) foreach(var a in nic.GetIPProperties().UnicastAddresses.Where(a=>a.Address.AddressFamily==AddressFamily.InterNetwork)) {
                try { udp.JoinMulticastGroup(IPAddress.Parse(Wire.Group),a.Address); } catch(SocketException) { }
            }
        } catch (NetworkInformationException) { }

        _=Task.Run(async()=> {
            while(!stop.IsCancellationRequested) try {
                var packet=await udp.ReceiveAsync(stop.Token);
                if(packet.Buffer.Length>128 || !Local(packet.RemoteEndPoint.Address) || Encoding.UTF8.GetString(packet.Buffer)!=Wire.DiscoveryMagic) continue;
                var response=System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new{version=1,id,name,port,fingerprint=Fingerprint},Wire.Json);
                await udp.SendAsync(response,packet.RemoteEndPoint,stop.Token);
            } catch(OperationCanceledException) {break;} catch(ObjectDisposedException) {break;} catch(SocketException) { }
        });
    }
    public async ValueTask DisposeAsync() { stop.Cancel();udp?.Dispose();if(app!=null){await app.StopAsync();await app.DisposeAsync();}stop.Dispose(); }
}
