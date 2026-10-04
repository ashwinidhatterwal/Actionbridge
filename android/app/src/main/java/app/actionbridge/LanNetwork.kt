package app.actionbridge
import android.content.Context
import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities
import java.net.Inet4Address
import java.net.InetAddress
object LanNetwork {
    fun find(context:Context,host:String?=null):Network? {
        val cm=context.getSystemService(Context.CONNECTIVITY_SERVICE) as ConnectivityManager
        val networks=cm.allNetworks.filter{net->cm.getNetworkCapabilities(net)?.let{it.hasTransport(NetworkCapabilities.TRANSPORT_WIFI)||it.hasTransport(NetworkCapabilities.TRANSPORT_ETHERNET)}==true}
        if(host!=null) {
            val address=runCatching{InetAddress.getByName(host)}.getOrNull()
            if(address is Inet4Address) {
                val target=number(address.address)
                networks.firstOrNull{network->cm.getLinkProperties(network)?.linkAddresses?.any{link->link.address is Inet4Address && sameSubnet(number(link.address.address),target,link.prefixLength)}==true}?.let{return it}
            }
        }
        return networks.firstOrNull()
    }
    fun number(bytes:ByteArray)=bytes.fold(0L){n,b->(n shl 8) or (b.toLong() and 255)}
    fun sameSubnet(a:Long,b:Long,prefix:Int):Boolean {if(prefix !in 1..32)return false;val mask=(0xffffffffL shl (32-prefix)) and 0xffffffffL;return (a and mask)==(b and mask)}
}
