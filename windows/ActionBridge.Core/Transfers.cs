using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
namespace ActionBridge.Core;
public sealed class Transfers {
    readonly string folder, downloads; readonly IActionSink sink;
    readonly ConcurrentDictionary<string, Job> jobs = new();
    readonly ConcurrentDictionary<string, SemaphoreSlim> gates = new();
    readonly SemaphoreSlim actionGate = new(1,1);
    public event Action<Job>? Changed;
    public Transfers(string folder, string downloads, IActionSink sink) {
        this.folder = folder; this.downloads = downloads; this.sink = sink;
        Directory.CreateDirectory(folder); Directory.CreateDirectory(downloads);
        foreach(var f in Directory.EnumerateFiles(folder, "*.json")) {
            var j = JsonSerializer.Deserialize<Job>(File.ReadAllText(f), Wire.Json) ?? throw new InvalidDataException("Invalid transfer journal.");
            Wire.Validate(j.Request);
            if (j.State == "running") { j.State = "uncertain"; j.Message = "PC restarted during action. Check the printer or application before sending again."; Save(j); }
            if (j.State == "uploading") {
                // Uncommitted bytes are discarded. Offset is the last durable checkpoint.
                if (File.Exists(Part(j))) { using var fs = File.Open(Part(j), FileMode.Open, FileAccess.Write); if(fs.Length < j.Offset) throw new InvalidDataException("Incomplete transfer journal."); fs.SetLength(j.Offset); }
                else if (j.Offset != 0) { j.State = "failed"; j.Message = "Partial file missing; send again."; Save(j); }
            }
            if(j.State != "uploading" && File.Exists(Part(j))) File.Delete(Part(j));
            jobs[j.Request.Id] = j;
        }
    }
    string Part(Job j) => Path.Combine(folder, j.Request.Id + ".part");
    public string Destination(Job j) => Path.Combine(downloads, j.Request.Id + "-" + Wire.SafeName(j.Request.Name));
    void Save(Job j) { var snapshot=Copy(j);snapshot.Updated=DateTime.UtcNow;Wire.AtomicJson(Path.Combine(folder,j.Request.Id+".json"),snapshot);j.Updated=snapshot.Updated; }
    static Job Copy(Job j)=>new(){Request=j.Request,ClientId=j.ClientId,Offset=j.Offset,State=j.State,Message=j.Message,Updated=j.Updated};
    void Commit(Job j,Job next){Save(next);j.Offset=next.Offset;j.State=next.State;j.Message=next.Message;j.Updated=next.Updated;}
    void Notify(Job j) { try { Changed?.Invoke(j); } catch { /* UI observers cannot break durable jobs. */ } }
    SemaphoreSlim Gate(string id) => gates.GetOrAdd(id,_=>new(1,1));
    public Job Get(string id,string client) {
        if (!jobs.TryGetValue(id,out var j) || j.ClientId != client) throw new KeyNotFoundException("Transfer not found."); return j;
    }
    public Job[] History() => jobs.Values.OrderByDescending(j=>j.Updated).Take(100).ToArray();
    public async Task<Job> Create(JobRequest r,string client) {
        Wire.Validate(r); var g=Gate(r.Id); await g.WaitAsync();
        try {
            if (jobs.TryGetValue(r.Id,out var old)) {
                if(old.ClientId!=client) throw new KeyNotFoundException("Transfer not found.");
                if(old.Request!=r) throw new ArgumentException("Transfer ID already has different content."); return old;
            }
            if(jobs.Values.Count(j=>j.State is "uploading" or "queued" or "running")>=128) throw new InvalidOperationException("Too many unfinished transfers on this PC. Finish existing transfers before sending more.");
            var j=new Job{Request=r,ClientId=client}; Save(j); jobs[r.Id]=j; Notify(j); return j;
        } finally { g.Release(); }
    }
    public async Task<Job> Append(string id,string client,long offset,Stream body,long length,CancellationToken ct) {
        _ = Get(id,client); var g=Gate(id); await g.WaitAsync(ct);
        try {
            var j=Get(id,client);
            if(j.State!="uploading" || offset!=j.Offset) throw new InvalidOperationException("Offset changed. Refresh transfer status.");
            if(length<0 || length>Wire.ChunkSize || offset+length>j.Request.Size) throw new ArgumentException("Invalid upload chunk.");
            if (new DriveInfo(Path.GetPathRoot(folder)!).AvailableFreeSpace < length + 64*1024*1024) throw new IOException("PC disk is almost full.");
            using var file=new FileStream(Part(j),FileMode.OpenOrCreate,FileAccess.Write,FileShare.None,65536,true);
            file.SetLength(offset); file.Position=offset;
            try {
                var buffer=new byte[65536]; long remaining=length;
                while(remaining>0) { var n=await body.ReadAsync(buffer.AsMemory(0,(int)Math.Min(buffer.Length,remaining)),ct); if(n==0) throw new EndOfStreamException(); await file.WriteAsync(buffer.AsMemory(0,n),ct); remaining-=n; }
                file.Flush(true); var next=Copy(j);next.Offset+=length;Commit(j,next);Notify(j); return j;
            } catch { file.SetLength(offset); file.Flush(true); throw; }
        } finally { g.Release(); }
    }
    public async Task<Job> Finish(string id,string client) {
        _ = Get(id,client); var g=Gate(id); await g.WaitAsync();
        try {
            var j=Get(id,client);
            if(j.State=="uploading") {
                if(j.Request.Action is not ("copy" or "url")) {
                    if(j.Offset!=j.Request.Size) throw new InvalidOperationException("Upload incomplete.");
                    var src=Part(j); var dest=Destination(j);
                    // Supports recovery if rename succeeded just before journal write.
                    if(!File.Exists(src) && !File.Exists(dest) && j.Request.Size==0) File.WriteAllBytes(src,Array.Empty<byte>());
                    using(var file=File.OpenRead(File.Exists(src)?src:dest)) {
                        var hash=Convert.ToHexString(await SHA256.HashDataAsync(file)).ToLowerInvariant();
                        if(hash!=j.Request.Sha256.ToLowerInvariant()) throw new InvalidDataException("File checksum mismatch. Send again.");
                    }
                    if (!File.Exists(dest)) {
                        // Final rename occurs on the destination volume, including redirected Downloads.
                        var staging=dest+".incoming";
                        try {
                            using (var source=File.OpenRead(src)) using (var output=new FileStream(staging,FileMode.Create,FileAccess.Write,FileShare.None,65536,true)) {
                                await source.CopyToAsync(output); output.Flush(true);
                            }
                            File.Move(staging,dest,false);
                        } catch {
                            // Preserve the resumable source, but release an unsuccessful destination copy.
                            try { File.Delete(staging); } catch { }
                            throw;
                        }
                    } else {
                        using var existing=File.OpenRead(dest);
                        if(Convert.ToHexString(await SHA256.HashDataAsync(existing)).ToLowerInvariant()!=j.Request.Sha256.ToLowerInvariant()) throw new InvalidDataException("Destination file changed. Send again.");
                    }
                }
                var next=Copy(j);next.State="queued";Commit(j,next); if(File.Exists(Part(j))) File.Delete(Part(j));
            }
            if(j.State=="queued") _=Run(j);
            return j;
        } finally { g.Release(); }
    }
    public async Task<Job> Cancel(string id,string client) {
        _ = Get(id,client); var gate=Gate(id); await gate.WaitAsync();
        try {
            var j=Get(id,client);
            if(j.State is "uploading" or "queued") {
                var next=Copy(j);next.State="cancelled";next.Message="Cancelled before the PC action started.";Commit(j,next);
                if(File.Exists(Part(j)))File.Delete(Part(j));Notify(j);
            }
            return j;
        } finally {gate.Release();}
    }
    async Task Run(Job j) {
        await actionGate.WaitAsync();
        try {
            // Serialize cancellation and the durable transition before starting a side effect.
            var gate=Gate(j.Request.Id); await gate.WaitAsync();
            try { if(j.State!="queued") return; var next=Copy(j);next.State="running";Commit(j,next); }
            finally { gate.Release(); }
            Notify(j);
            var result=Copy(j);
            try { await sink.ExecuteAsync(j.Request,j.Request.Action is "copy" or "url" ? null : Destination(j)); result.State=j.Request.Action=="print" ? "submitted" : "completed"; result.Message=j.Request.Action=="print" ? "Submitted to Windows printer queue; physical printing is not confirmed." : "Action completed."; }
            catch(Exception ex) { result.State=j.Request.Action=="print" ? "uncertain" : "failed"; result.Message=ex.Message; }
            Commit(j,result); Notify(j);
        } finally { actionGate.Release(); }
    }
}
