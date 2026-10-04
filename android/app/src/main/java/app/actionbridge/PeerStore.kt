package app.actionbridge
import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import org.json.JSONObject
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec
import java.util.UUID

data class Peer(val id:String,val name:String,val host:String,val port:Int,val fingerprint:String) {
    fun json() = JSONObject().put("id",id).put("name",name).put("host",host).put("port",port).put("fingerprint",fingerprint)
    companion object {fun parse(j:JSONObject)=Peer(j.getString("id"),j.getString("name"),j.getString("host"),j.getInt("port"),j.getString("fingerprint"))}
}
class PeerStore(context:Context) {
    private val prefs=context.getSharedPreferences("identity",Context.MODE_PRIVATE)
    private fun key():SecretKey {
        val ks=KeyStore.getInstance("AndroidKeyStore").apply{load(null)}
        (ks.getKey("bridge-storage",null) as? SecretKey)?.let{return it}
        return KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES,"AndroidKeyStore").apply{
            init(KeyGenParameterSpec.Builder("bridge-storage",KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT).setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).build())
        }.generateKey()
    }
    fun clientId():String = synchronized(lock) { prefs.getString("client",null) ?: UUID.randomUUID().toString().also{prefs.edit().putString("client",it).commit()} }
    private fun read(id:String):JSONObject? {
        val value=prefs.getString("peer-$id",null)?:return null
        val bytes=Base64.decode(value,Base64.NO_WRAP)
        val c=Cipher.getInstance("AES/GCM/NoPadding");c.init(Cipher.DECRYPT_MODE,key(),GCMParameterSpec(128,bytes.copyOfRange(0,12)))
        return JSONObject(String(c.doFinal(bytes.copyOfRange(12,bytes.size)),Charsets.UTF_8))
    }
    fun get(id:String):JSONObject? = synchronized(lock) {read(id)}
    fun save(peer:Peer,token:String,approved:Boolean) = synchronized(lock) {
        val data=peer.json().put("token",token).put("approved",approved)
        val cipher=Cipher.getInstance("AES/GCM/NoPadding");cipher.init(Cipher.ENCRYPT_MODE,key())
        check(prefs.edit().putString("peer-${peer.id}",Base64.encodeToString(cipher.iv+cipher.doFinal(data.toString().toByteArray()),Base64.NO_WRAP)).commit()) {"Could not store PC identity."}
    }
    fun remembered():List<Peer> = synchronized(lock){prefs.all.keys.filter{it.startsWith("peer-")&&!it.startsWith("peer-remote-")}.mapNotNull{runCatching{read(it.removePrefix("peer-"))?.let(Peer::parse)}.getOrNull()}}
    fun remote(id:String):JSONObject?=synchronized(lock){read("remote-$id")}
    fun remotes():List<Peer> = synchronized(lock){prefs.all.keys.filter{it.startsWith("peer-remote-")}.mapNotNull{runCatching{read(it.removePrefix("peer-"))?.let(Peer::parse)}.getOrNull()}}
    fun saveRemote(encoded:String):Peer=synchronized(lock){
        val payload=JSONObject(String(Base64.decode(encoded,Base64.URL_SAFE or Base64.NO_WRAP or Base64.NO_PADDING),Charsets.UTF_8))
        val id=payload.optString("id").takeIf{runCatching{UUID.fromString(it)}.isSuccess}?:payload.getString("room")
        val peer=Peer(id,payload.optString("name","Your PC"),"",45833,payload.optString("fingerprint"))
        val cipher=Cipher.getInstance("AES/GCM/NoPadding");cipher.init(Cipher.ENCRYPT_MODE,key())
        val data=peer.json().put("code",encoded)
        check(prefs.edit().putString("peer-remote-$id",Base64.encodeToString(cipher.iv+cipher.doFinal(data.toString().toByteArray()),Base64.NO_WRAP)).commit())
        // Upgrade a v0.3 room-only entry to the PC's stable local identity.
        prefs.all.keys.filter{it.startsWith("peer-remote-")&&it!="peer-remote-$id"}.forEach{entry->runCatching{val old=read(entry.removePrefix("peer-"))?:return@runCatching;val previous=JSONObject(String(Base64.decode(old.getString("code"),Base64.URL_SAFE or Base64.NO_WRAP or Base64.NO_PADDING),Charsets.UTF_8));if(previous.optString("room")==payload.optString("room")&&previous.optString("service")==payload.optString("service"))prefs.edit().remove(entry).commit()}}
        setLast(id);peer
    }
    fun forget(id:String) = synchronized(lock){prefs.edit().remove("peer-$id").remove("peer-remote-$id").commit()}
    fun last():String?=prefs.getString("last",null)
    fun setLast(id:String){prefs.edit().putString("last",id).apply()}
    companion object {private val lock=Any()}
}
