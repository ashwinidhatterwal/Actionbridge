using System.Collections.Concurrent;
namespace ActionBridge.Core;
/// Live presence is derived only from authenticated traffic, never from a saved pairing.
public sealed class DevicePresence(TimeProvider? clock=null) {
    readonly TimeProvider clock=clock??TimeProvider.System;
    readonly ConcurrentDictionary<string,DateTimeOffset> seen=new();
    public void Touch(string id)=>seen[id]=clock.GetUtcNow();
    public bool Connected(string id)=>seen.TryGetValue(id,out var at)&&clock.GetUtcNow()-at<TimeSpan.FromSeconds(15);
    public void Remove(string id)=>seen.TryRemove(id,out _);
}
