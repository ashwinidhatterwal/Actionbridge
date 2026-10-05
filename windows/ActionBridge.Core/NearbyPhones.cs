using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Text.Json;
namespace ActionBridge.Core;
public sealed record NearbyPhone(string Id,string Name,string Host,int Port,string Nonce) {
    public override string ToString()=>Name+" · Phone · "+Host;
}
/// Discovery is untrusted metadata. Invitations always require approval on both devices.
public static class NearbyPhones {
    public const int Port=45834;
    public static async Task<NearbyPhone[]> Find(CancellationToken ct=default,IEnumerable<IPAddress>? targets=null) {
        using var udp=new UdpClient(AddressFamily.InterNetwork);udp.EnableBroadcast=true;
        udp.Client.Bind(new IPEndPoint(IPAddress.Any,0));
        var nonce=Guid.NewGuid().ToString("N");
        var request=JsonSerializer.SerializeToUtf8Bytes(new{v=1,action="find",nonce});
        var found=new Dictionary<string,NearbyPhone>();
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(3200);
        var addresses=(targets??Broadcasts()).Distinct().ToArray();
        var send=Task.Run(async()=>{try{while(!timeout.IsCancellationRequested){foreach(var address in addresses)try{await udp.SendAsync(request,new IPEndPoint(address,Port),timeout.Token);}catch(SocketException){}await Task.Delay(800,timeout.Token);}}catch(OperationCanceledException){}},CancellationToken.None);
        try{while(!timeout.IsCancellationRequested){var packet=await udp.ReceiveAsync(timeout.Token);if(packet.Buffer.Length>2048||!BridgeServer.Local(packet.RemoteEndPoint.Address))continue;
            try{var j=JsonSerializer.Deserialize<JsonElement>(packet.Buffer);var id=j.GetProperty("id").GetString()!;var name=j.GetProperty("name").GetString()!;
                if(j.GetProperty("v").GetInt32()!=1||j.GetProperty("nonce").GetString()!=nonce||!Guid.TryParseExact(id,"D",out _)||string.IsNullOrWhiteSpace(name)||name.Length>80||found.Count>=48)continue;
                found[id]=new(id,name,packet.RemoteEndPoint.Address.ToString(),Port,nonce);
            }catch(Exception e)when(e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException){}
        }}catch(OperationCanceledException)when(!ct.IsCancellationRequested){}finally{timeout.Cancel();await send;}
        return found.Values.ToArray();
    }
    static IPAddress[] Broadcasts(){var addresses=new List<IPAddress>{IPAddress.Broadcast};try{foreach(var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==OperationalStatus.Up))foreach(var a in nic.GetIPProperties().UnicastAddresses.Where(a=>a.Address.AddressFamily==AddressFamily.InterNetwork&&BridgeServer.Local(a.Address)&&!IPAddress.IsLoopback(a.Address))){var ip=a.Address.GetAddressBytes();var mask=a.IPv4Mask.GetAddressBytes();addresses.Add(new IPAddress(ip.Select((b,i)=>(byte)(b|~mask[i])).ToArray()));}}catch(NetworkInformationException){}return addresses.ToArray();}
    public static async Task Invite(NearbyPhone phone,object computer,CancellationToken ct=default){
        if(phone.Port!=Port||!Guid.TryParseExact(phone.Id,"D",out _)||phone.Nonce.Length!=32||!phone.Nonce.All(Uri.IsHexDigit))throw new ArgumentException("Invalid discovered phone.");
        ComputerRules.Local(phone.Host,phone.Port);
        using var udp=new UdpClient(AddressFamily.InterNetwork);
        var bytes=JsonSerializer.SerializeToUtf8Bytes(new{v=1,action="invite",nonce=phone.Nonce,target=phone.Id,computer},Wire.JsonCompact);
        await udp.SendAsync(bytes,new IPEndPoint(IPAddress.Parse(phone.Host),phone.Port),ct);
    }
}
