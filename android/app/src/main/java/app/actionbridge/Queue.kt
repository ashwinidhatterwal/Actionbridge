package app.actionbridge
import android.content.Context
import android.net.Uri
import android.provider.OpenableColumns
import androidx.work.*
import org.json.JSONObject
import java.io.File
import java.security.MessageDigest
import java.util.UUID
import java.util.concurrent.TimeUnit
object Queue {
    private val lock=Any()
    fun directory(c:Context)=File(c.filesDir,"queue").apply{mkdirs()}
    fun metadata(c:Context,id:String)=File(directory(c),"$id.json")
    fun write(file:File,json:JSONObject) = synchronized(lock) {
        val temp=File(file.path+".new");java.io.FileOutputStream(temp).use{it.write(json.toString().toByteArray());it.fd.sync()}
        check(temp.renameTo(file)){"Could not save transfer."}
    }
    fun state(c:Context,id:String,state:String,message:String,progress:Int=0) = synchronized(lock) {
        val file=metadata(c,id);if(!file.exists())return@synchronized
        val json=JSONObject(file.readText());if(TransferStateRules.ignoreUpdate(json.optString("state"),state))return@synchronized;json.put("state",state).put("message",message).put("progress",progress);write(file,json)
        if(TransferStateRules.releasePayload(state))File(directory(c),"$id.payload").delete()
    }
    fun list(c:Context):List<JSONObject> = synchronized(lock){directory(c).listFiles()?.filter{it.extension=="json"}?.sortedByDescending{it.lastModified()}?.take(50)?.mapNotNull{runCatching{JSONObject(it.readText())}.getOrNull()}?:emptyList()}
    fun enqueue(c:Context,id:String) {
        val work=OneTimeWorkRequestBuilder<TransferWorker>().setInputData(workDataOf("id" to id)).setBackoffCriteria(BackoffPolicy.EXPONENTIAL,30,TimeUnit.SECONDS).addTag("bridge").build()
        WorkManager.getInstance(c).enqueueUniqueWork("bridge-$id",ExistingWorkPolicy.KEEP,work)
    }
    fun cancel(c:Context,id:String) {
        state(c,id,"cancelled","Cancelled on this phone. An action already submitted to the PC may still run.")
        WorkManager.getInstance(c).cancelUniqueWork("bridge-$id")
        // Also release the PC partial when reachable, including paused uploads.
        java.util.concurrent.Executors.newSingleThreadExecutor().also{executor->executor.execute{try{
            val j=JSONObject(metadata(c,id).readText());val peer=Peer.parse(j.getJSONObject("peer"));val identity=PeerStore(c).get(peer.id)
            if(identity!=null)BridgeApi(peer,identity.getString("token"),c).request("/jobs/$id/cancel","POST",JSONObject())
        }catch(_:Exception){}finally{executor.shutdown()}}}
    }
    fun cleanup(c:Context) {
        // Delete only terminal payloads older than seven days. Active uploads remain resumable.
        val cutoff=System.currentTimeMillis()-7L*24*60*60*1000
        directory(c).listFiles()?.filter{it.extension=="payload" && it.lastModified()<cutoff && !File(directory(c),it.nameWithoutExtension+".json").exists()}?.forEach{it.delete()}
        directory(c).listFiles()?.filter{it.extension=="json"&&it.lastModified()<cutoff}?.forEach {file->runCatching{
            val j=JSONObject(file.readText());if(j.optString("state") in setOf("completed","submitted","cancelled","failed","uncertain")){File(directory(c),j.getString("id")+".payload").delete();file.delete()}
        }}
    }
    fun stage(c:Context,uri:Uri?,text:String?,peer:Peer,action:String,options:JSONObject,onProgress:(Long)->Unit):String {
        val id=UUID.randomUUID().toString();val file=File(directory(c),"$id.payload")
        var name=if(action=="url")"Web link" else "Text";var size=0L;var hash=""
        if(uri!=null) {
            check(uri.scheme=="content"){"Share files through an Android app or the file picker."}
            c.contentResolver.query(uri,arrayOf(OpenableColumns.DISPLAY_NAME,OpenableColumns.SIZE),null,null,null)?.use{cursor->if(cursor.moveToFirst()){val n=cursor.getColumnIndex(OpenableColumns.DISPLAY_NAME);if(n>=0)name=cursor.getString(n)?:"file";val s=cursor.getColumnIndex(OpenableColumns.SIZE);if(s>=0&&!cursor.isNull(s)&&cursor.getLong(s)>2147483648L)error("Files must be at most 2 GB.")}}
            if(name=="Text")name=uri.lastPathSegment?.substringAfterLast('/')?:"file"
            if(action=="print") {
                val extension=name.substringAfterLast('.',"").lowercase()
                require(extension in listOf("pdf","jpg","jpeg","png","bmp")){"Printing supports PDF, JPG, PNG and BMP files."}
                require(options.optInt("pageFrom")==0 || extension=="pdf"){"A page range is only available for PDFs. Clear the range for photos."}
            }
            val digest=MessageDigest.getInstance("SHA-256")
            try {
                val input=c.contentResolver.openInputStream(uri)?:error("Cannot read this file.")
                input.use{source->java.io.FileOutputStream(file).use {dest->
                    val buf=ByteArray(65536);while(true){val n=source.read(buf);if(n<0)break;size+=n;if(size>2147483648L)error("Files must be at most 2 GB.");if(file.parentFile!!.usableSpace<64*1024*1024L)error("Phone storage is almost full.");dest.write(buf,0,n);digest.update(buf,0,n);onProgress(size)};dest.fd.sync()
                }}
                hash=digest.digest().joinToString(""){"%02x".format(it)}
            }catch(e:Exception){file.delete();throw e}
        }
        val request=JSONObject().put("id",id).put("name",name.take(512)).put("size",size).put("sha256",hash).put("action",action)
        if(text!=null)request.put("text",text)
        options.keys().forEach{request.put(it,options.get(it))}
        write(metadata(c,id),JSONObject().put("id",id).put("peer",peer.json()).put("request",request).put("state","queued").put("message","Waiting to send").put("progress",0))
        return id
    }
}
