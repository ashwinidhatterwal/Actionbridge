package app.actionbridge
import android.content.Context
import android.content.ContentValues
import android.net.Uri
import android.os.Build
import android.provider.MediaStore
import java.util.Base64
import androidx.core.content.FileProvider
import org.json.JSONObject
import java.io.File
import java.io.RandomAccessFile
import java.security.MessageDigest
import java.util.UUID

/** Authenticated transport calls this store. IDs, bounds and hashes are checked here too. */
class Incoming(private val context:Context) {
 companion object {private val lock=Any()}
 private val root=File(context.filesDir,"inbox").apply{mkdirs()}
 private fun id(j:JSONObject):String=j.getString("id").also{require(UUID.fromString(it).toString()==it)}
 private fun meta(id:String)=File(root,"$id.json")
 private fun payload(id:String)=File(root,"$id.payload")
 fun begin(value:String,pc:String):String = synchronized(lock) {
  val j=JSONObject(value);val id=id(j);val kind=j.getString("kind");val size=j.getLong("size")
  IncomingRules.validate(j)
  if(meta(id).exists()){val old=JSONObject(meta(id).readText());require(old.getString("pc")==pc&&old.getLong("size")==size&&old.optString("sha256")==j.optString("sha256")&&old.getString("kind")==kind&&old.optString("text")==j.optString("text"));if(old.optBoolean("completed")){payload(id).delete();return "done"}}
  else {j.put("pc",pc).put("completed",false).put("receivedAt",System.currentTimeMillis());Queue.write(meta(id),j)}
  val n=if(payload(id).exists())payload(id).length() else 0L;require(n<=size);return n.toString()
 }
 fun append(id:String,offset:Long,encoded:String):Long = synchronized(lock) {
  require(UUID.fromString(id).toString()==id&&encoded.length<=350000)
  val j=JSONObject(meta(id).readText());require(!j.optBoolean("completed")&&j.getString("kind")=="file")
  val bytes=Base64.getDecoder().decode(encoded);require(bytes.size in 1..262144&&offset>=0&&offset+bytes.size<=j.getLong("size"))
  RandomAccessFile(payload(id),"rw").use{f->require(f.length()==offset);f.seek(offset);f.write(bytes);f.fd.sync();return f.length()}
 }
 fun finish(id:String):Boolean = synchronized(lock) {
  require(UUID.fromString(id).toString()==id);val j=JSONObject(meta(id).readText());if(j.optBoolean("completed"))return false
  if(j.getString("kind")=="file"){
   val f=payload(id);if(!f.exists())f.createNewFile();require(f.length()==j.getLong("size"))
   val digest=MessageDigest.getInstance("SHA-256");f.inputStream().use{input->val b=ByteArray(262144);while(true){val n=input.read(b);if(n<0)break;digest.update(b,0,n)}}
   if(digest.digest().joinToString(""){"%02x".format(it)}!=j.getString("sha256")){f.delete();error("File integrity check failed. Retry the file.")}
   val name=IncomingRules.safeName(j.getString("name"))
   if(Build.VERSION.SDK_INT>=29){
    val resolver=context.contentResolver;var uri=j.optString("uri").takeIf{it.isNotEmpty()}?.let(Uri::parse)
    if(uri!=null){
     val pending=resolver.query(uri,arrayOf(MediaStore.Downloads.IS_PENDING),null,null,null)?.use{if(it.moveToFirst())it.getInt(0) else -1}?:error("Could not inspect the received download. Retry when storage is available.")
     if(pending==0){j.put("completed",true);Queue.write(meta(id),j);f.delete();return true}
     if(pending==-1){j.remove("uri");Queue.write(meta(id),j);uri=null}
    }
    if(uri==null){val values=ContentValues().apply{put(MediaStore.Downloads.DISPLAY_NAME,name);put(MediaStore.Downloads.MIME_TYPE,"application/octet-stream");put(MediaStore.Downloads.RELATIVE_PATH,"Download/ActionBridge");put(MediaStore.Downloads.IS_PENDING,1)};uri=resolver.insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI,values)?:error("Could not save received file");j.put("uri",uri.toString());Queue.write(meta(id),j)}
    resolver.openOutputStream(uri,"wt")!!.use{out->f.inputStream().use{it.copyTo(out)}};check(resolver.update(uri,ContentValues().apply{put(MediaStore.Downloads.IS_PENDING,0)},null,null)>0){"Download publication was not confirmed. Retry to finish saving."}
   }else{
    val destination=File(context.getExternalFilesDir(null),"Received").apply{mkdirs()};val saved=File(destination,"${id.take(8)}-$name");f.copyTo(saved,overwrite=true);j.put("uri",FileProvider.getUriForFile(context,"${context.packageName}.received",saved).toString())
   }
   j.put("name",name)
  }
  j.remove("error");j.put("completed",true);Queue.write(meta(id),j);payload(id).delete();return true
 }
 fun failure(id:String,message:String)=synchronized(lock){runCatching{require(UUID.fromString(id).toString()==id);val j=JSONObject(meta(id).readText());j.put("error",message.take(300));Queue.write(meta(id),j)}}
 fun list():List<JSONObject> = synchronized(lock){root.listFiles()?.filter{it.extension=="json"}?.mapNotNull{runCatching{JSONObject(it.readText())}.getOrNull()}?.sortedByDescending{it.optLong("receivedAt")}?.take(30)?:emptyList()}
}
