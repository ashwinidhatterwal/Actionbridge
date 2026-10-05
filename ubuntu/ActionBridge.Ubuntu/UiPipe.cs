using System.Collections.Concurrent;
using System.Text.Json;
using ActionBridge.Core;
namespace ActionBridge.Ubuntu;
// Private inherited pipes only; no browser or extra control socket.
public sealed class UiPipe {
 readonly object output=new();readonly ConcurrentDictionary<string,TaskCompletionSource<JsonElement>> pending=new();
 public void Send(object value){var text=JsonSerializer.Serialize(value,Wire.JsonCompact);lock(output){Console.WriteLine(text);Console.Out.Flush();}}
 public void Event(string name,object value)=>Send(new{ @event=name,data=value});
 public async Task<JsonElement> Request(string name,object value){var id=Guid.NewGuid().ToString();var t=new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);pending[id]=t;Send(new{@event=name,data=value,uiId=id});try{return await t.Task.WaitAsync(TimeSpan.FromSeconds(55));}finally{pending.TryRemove(id,out _);}}
 public bool Reply(JsonElement message){if(!message.TryGetProperty("uiReply",out var id))return false;if(pending.TryGetValue(id.GetString()!,out var t)){if(message.TryGetProperty("error",out var error))t.TrySetException(new InvalidOperationException(error.GetString()));else t.TrySetResult(message.GetProperty("result").Clone());}return true;}
 public void Close(){foreach(var t in pending.Values)t.TrySetException(new IOException("Desktop session disconnected."));pending.Clear();}
}
