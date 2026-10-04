using System.Text.Json;
namespace ActionBridge.Core;
// Only operations already exposed to an approved LAN phone are available remotely.
public sealed class RemoteJobs(Transfers transfers,IActionSink sink,string room,Outbox? outbox=null) {
    readonly string client="remote:"+room;
    static object Status(Job j)=>new{request=new{id=j.Request.Id,size=j.Request.Size,action=j.Request.Action},j.Offset,j.State,message=j.Message};
    public async Task<object> Dispatch(JsonElement message) {
        var method=message.GetProperty("method").GetString();
        if(method=="outbox")return outbox?.List(client)??[];
        if(method=="download"){var count=message.GetProperty("count").GetInt32();if(count is <1 or >16384)throw new ArgumentException("Remote download chunk too large.");return outbox?.Read(message.GetProperty("itemId").GetString()!,client,message.GetProperty("offset").GetInt64(),message.GetProperty("count").GetInt32())??throw new InvalidOperationException("Update the PC app.");}
        if(method=="received")return outbox?.Acknowledge(message.GetProperty("itemId").GetString()!,client)??throw new InvalidOperationException("Update the PC app.");
        if(method=="printers")return sink.Printers();
        if(method=="create")return Status(await transfers.Create(message.GetProperty("job").Deserialize<JobRequest>(Wire.Json)??throw new ArgumentException("Missing job"),client));
        if(method is not ("status" or "finish" or "cancel" or "append"))throw new ArgumentException("Unsupported remote action.");
        var id=message.GetProperty("jobId").GetString()??"";
        switch(method) {
            case "status":return Status(transfers.Get(id,client));
            case "finish":return Status(await transfers.Finish(id,client));
            case "cancel":return Status(await transfers.Cancel(id,client));
            case "append":
                var encoded=message.GetProperty("data").GetString()??"";
                if(encoded.Length>360000)throw new ArgumentException("Remote chunk too large.");
                var data=Convert.FromBase64String(encoded);if(data.Length>262144)throw new ArgumentException("Remote chunk too large.");
                return Status(await transfers.Append(id,client,message.GetProperty("offset").GetInt64(),new MemoryStream(data,false),data.Length,CancellationToken.None));
            default:throw new ArgumentException("Unsupported remote action.");
        }
    }
}
