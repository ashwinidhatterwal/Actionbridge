package app.actionbridge
import android.content.Context
import android.net.ConnectivityManager
import android.net.wifi.WifiManager
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.net.*
class Discovery(private val context:Context) {
    suspend fun scan(seconds:Int=3):List<Peer> = withContext(Dispatchers.IO) {
        val found=linkedMapOf<String,Peer>()
        val lock=(context.applicationContext.getSystemService(Context.WIFI_SERVICE) as WifiManager).createMulticastLock("ActionBridge").apply{setReferenceCounted(false);acquire()}
        try {
            DatagramSocket().use {socket->
                LanNetwork.find(context)?.bindSocket(socket)
                socket.broadcast=true;socket.soTimeout=250
                val targets=linkedSetOf("255.255.255.255","239.255.42.99")
                val cm=context.getSystemService(Context.CONNECTIVITY_SERVICE) as ConnectivityManager
                cm.allNetworks.forEach {net->cm.getLinkProperties(net)?.linkAddresses?.forEach{a->
                    if(a.address is Inet4Address && a.prefixLength in 1..30) {
                        val bytes=a.address.address;val ip=bytes.fold(0L){n,b->(n shl 8) or (b.toLong() and 255)}
                        val mask=(0xffffffffL shl (32-a.prefixLength)) and 0xffffffffL
                        val broadcast=ip or (mask xor 0xffffffffL)
                        targets.add((3 downTo 0).joinToString("."){i->((broadcast shr (i*8)) and 255).toString()})
                    }
                }}
                // Known endpoints also get probed; this covers some routers filtering broadcasts.
                PeerStore(context).remembered().forEach{targets.add(it.host)}
                val magic="ACTIONBRIDGE_DISCOVER_V1".toByteArray();val deadline=System.currentTimeMillis()+seconds*1000;var nextSend=0L
                while(System.currentTimeMillis()<deadline) {
                    if(System.currentTimeMillis()>=nextSend){targets.forEach{host->runCatching{socket.send(DatagramPacket(magic,magic.size,InetAddress.getByName(host),45832))}};nextSend=System.currentTimeMillis()+900}
                    val packet=DatagramPacket(ByteArray(2048),2048)
                    try {socket.receive(packet);val j=JSONObject(String(packet.data,0,packet.length));
                        if(j.optInt("version")==1 && j.optInt("port")==45833 && j.optString("fingerprint").matches(Regex("[a-f0-9]{64}")) && runCatching{java.util.UUID.fromString(j.getString("id"))}.isSuccess && j.getString("name").length<=80) {
                            val peer=Peer(j.getString("id"),j.getString("name"),packet.address.hostAddress!!,j.getInt("port"),j.getString("fingerprint"))
                            found[peer.id]=peer
                        }
                    } catch(_:SocketTimeoutException) {} catch(_:org.json.JSONException) {}
                }
            }
            found.values.toList()
        } finally {if(lock.isHeld)lock.release()}
    }
}
