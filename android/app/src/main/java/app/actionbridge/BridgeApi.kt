package app.actionbridge
import org.json.JSONObject
import java.io.IOException
import java.security.MessageDigest
import java.security.SecureRandom
import java.security.cert.X509Certificate
import javax.net.ssl.*
import java.net.URL
class ApiException(val code:Int,message:String):IOException(message)
class BridgeApi(private val peer:Peer,private val token:String?=null,private val context:android.content.Context?=null) {
    private val tls=SSLContext.getInstance("TLS").apply {
        init(null,arrayOf(object:X509TrustManager {
            override fun getAcceptedIssuers()=emptyArray<X509Certificate>()
            override fun checkClientTrusted(chain:Array<X509Certificate>,auth:String){throw java.security.cert.CertificateException("Not a server")}
            override fun checkServerTrusted(chain:Array<X509Certificate>,auth:String) {
                val fingerprint=MessageDigest.getInstance("SHA-256").digest(chain.first().encoded).joinToString(""){"%02x".format(it)}
                if(fingerprint!=peer.fingerprint)throw java.security.cert.CertificateException("PC identity changed. Remove and approve this PC again.")
                chain.first().checkValidity()
            }
        }),SecureRandom())
    }
    fun hello():String {
        var last:IOException?=null
        for(attempt in 0..2) {
            try{return request("/hello")}catch(e:IOException){
                if(e is ApiException || e is SSLHandshakeException)throw e
                last=e
                if(attempt<2)Thread.sleep(350L*(attempt+1))
            }
        }
        throw last!!
    }
    fun request(path:String,method:String="GET",json:JSONObject?=null,bytes:ByteArray?=null):String {
        val url=URL("https://${peer.host}:${peer.port}/v1$path")
        val network=context?.let{LanNetwork.find(it,peer.host)}
        val connection=(network?.openConnection(url,java.net.Proxy.NO_PROXY)?:url.openConnection(java.net.Proxy.NO_PROXY)) as HttpsURLConnection
        connection.sslSocketFactory=tls.socketFactory
        // The exact certificate is checked by the trust manager; LAN IPs change.
        connection.hostnameVerifier=HostnameVerifier{_,_->true}
        connection.connectTimeout=5000;connection.readTimeout=if(path=="/pair")65000 else 45000
        connection.requestMethod=method;connection.instanceFollowRedirects=false
        token?.let{connection.setRequestProperty("Authorization","Bearer $it")}
        try {
            val payload=bytes?:json?.toString()?.toByteArray()
            if(payload!=null){connection.doOutput=true;connection.setRequestProperty("Content-Type",if(bytes!=null)"application/octet-stream" else "application/json");connection.setFixedLengthStreamingMode(payload.size);connection.outputStream.use{it.write(payload)}}
            val code=connection.responseCode
            val response=(if(code in 200..299)connection.inputStream else connection.errorStream)?.use {input->
                val out=java.io.ByteArrayOutputStream();val buffer=ByteArray(8192);while(true){val n=input.read(buffer);if(n<0)break;if(out.size()+n>1024*1024)throw IOException("PC response too large.");out.write(buffer,0,n)};out.toString("UTF-8")
            }?:"{}"
            if(code !in 200..299)throw ApiException(code,runCatching{JSONObject(response).optString("error","PC returned $code")}.getOrDefault("PC returned $code"))
            return response
        } finally {connection.disconnect()}
    }
}
