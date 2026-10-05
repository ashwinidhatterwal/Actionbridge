using ActionBridge.Core;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
var checks=0;
void Check(bool value,string name){if(!value)throw new Exception(name);checks++;}
var clock=new TestClock();var presence=new DevicePresence(clock);
Check(!presence.Connected("saved"),"Saved identity must not imply a live connection");
presence.Touch("saved");Check(presence.Connected("saved"),"Authenticated traffic establishes presence");
clock.Advance(14);Check(presence.Connected("saved"),"Presence tolerates several polling intervals");
clock.Advance(1);Check(!presence.Connected("saved"),"Connection expires without traffic");
presence.Touch("saved");Check(presence.Connected("saved"),"Reconnection restores presence");
presence.Remove("saved");Check(!presence.Connected("saved"),"Revocation clears presence");
using var phone=new UdpClient(new IPEndPoint(IPAddress.Any,NearbyPhones.Port));
using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(10));
var id=Guid.NewGuid().ToString();
var listener=Task.Run(async()=>{var request=await phone.ReceiveAsync(stop.Token);var j=JsonSerializer.Deserialize<JsonElement>(request.Buffer);var nonce=j.GetProperty("nonce").GetString();
 await phone.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new{v=1,id,name="Wrong nonce",nonce="bad"}),request.RemoteEndPoint,stop.Token);
 await phone.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new{v=1,id,name="Test phone",nonce}),request.RemoteEndPoint,stop.Token);
});
var found=await NearbyPhones.Find(stop.Token,[IPAddress.Loopback]);await listener;
Check(found.Length==1&&found[0].Name=="Test phone","Discovery accepts only correlated responses");
var target=found[0] with {Host="127.0.0.1"};
await NearbyPhones.Invite(target,new {id=Guid.NewGuid().ToString(),name="Computer",port=45833,fingerprint=new string('a',64)},stop.Token);
JsonElement message;do{var invite=await phone.ReceiveAsync(stop.Token);message=JsonSerializer.Deserialize<JsonElement>(invite.Buffer);}while(!message.TryGetProperty("action",out var action)||action.GetString()!="invite");
Check(message.GetProperty("target").GetString()==id&&message.GetProperty("nonce").GetString()==target.Nonce,"Invitation binds to the discovered phone and nonce");
Console.WriteLine($"Passed {checks} presence and discovery checks.");
class TestClock:TimeProvider {DateTimeOffset now=DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow()=>now;public void Advance(int seconds)=>now=now.AddSeconds(seconds);}
