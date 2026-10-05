package app.actionbridge

import android.content.Context
import android.net.wifi.WifiManager
import android.os.Build
import android.os.SystemClock
import org.json.JSONObject
import java.net.*
import java.util.UUID

/** Visible only while the app is in the foreground. Discovery never grants access. */
class NearbyPhone(private val context:Context,private val invite:(Peer)->Unit) {
    private var socket:DatagramSocket?=null
    private var lock:WifiManager.MulticastLock?=null
    @Synchronized fun start() {
        if(socket!=null)return
        val server=DatagramSocket(null)
        try {
            server.reuseAddress=true;server.bind(InetSocketAddress(45834));server.soTimeout=1000
            LanNetwork.find(context)?.bindSocket(server)
            lock=(context.applicationContext.getSystemService(Context.WIFI_SERVICE) as WifiManager).createMulticastLock("ActionBridge nearby phone").apply{setReferenceCounted(false);acquire()}
            socket=server
            val id=PeerStore(context).clientId()
            Thread({
                val challenges=mutableMapOf<String,Pair<String,Long>>()
                var lastInvite=-15000L
                while(!server.isClosed)try {
                    val packet=DatagramPacket(ByteArray(4096),4096);server.receive(packet)
                    if(packet.length>2048||!privateAddress(packet.address))continue
                    val j=JSONObject(String(packet.data,0,packet.length));if(j.optInt("v")!=1)continue
                    val now=SystemClock.elapsedRealtime();val host=packet.address.hostAddress?:continue
                    challenges.entries.removeAll{now-it.value.second>30000}
                    when(j.optString("action")) {
                        "find"->{val nonce=j.optString("nonce");if(!nonce.matches(Regex("[a-f0-9]{32}"))||challenges.size>=48)continue
                            challenges[host]=nonce to now
                            val bytes=JSONObject().put("v",1).put("id",id).put("name",Build.MODEL.take(80)).put("nonce",nonce).toString().toByteArray()
                            server.send(DatagramPacket(bytes,bytes.size,packet.socketAddress))}
                        "invite"->{if(j.optString("target")!=id||challenges[host]?.first!=j.optString("nonce")||now-lastInvite<15000)continue
                            val c=j.getJSONObject("computer");val pcId=c.getString("id");val name=c.getString("name");val pin=c.getString("fingerprint")
                            if(runCatching{UUID.fromString(pcId)}.isFailure||name.isBlank()||name.length>80||c.optInt("port")!=45833||!pin.matches(Regex("[a-f0-9]{64}")))continue
                            challenges.remove(host);lastInvite=now;invite(Peer(pcId,name,host,45833,pin))}
                    }
                }catch(_:SocketTimeoutException){}catch(_:Exception){if(server.isClosed)break}
            },"ActionBridge nearby").apply{isDaemon=true;start()}
        }catch(_:Exception){server.close();lock?.let{if(it.isHeld)it.release()};lock=null}
    }
    @Synchronized fun stop(){socket?.close();socket=null;lock?.let{if(it.isHeld)it.release()};lock=null}
    companion object {
        fun privateAddress(a:InetAddress):Boolean {
            if(a.isLoopbackAddress||a.isLinkLocalAddress)return true
            val b=a.address.map{it.toInt() and 255}
            return if(b.size==4)b[0]==10||b[0]==192&&b[1]==168||b[0]==172&&b[1] in 16..31 else (b[0] and 254)==252
        }
    }
}
