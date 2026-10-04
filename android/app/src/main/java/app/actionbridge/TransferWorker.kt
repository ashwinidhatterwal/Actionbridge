package app.actionbridge
import android.app.*
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Build
import androidx.work.*
import kotlinx.coroutines.*
import org.json.JSONObject
import java.io.File
import java.io.RandomAccessFile
import java.io.IOException
class TransferWorker(context:Context,params:WorkerParameters):CoroutineWorker(context,params) {
    private val jobId=inputData.getString("id")?:""
    private fun info(message:String,percent:Int):ForegroundInfo {
        val manager=applicationContext.getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
        manager.createNotificationChannel(NotificationChannel("transfers","File transfers",NotificationManager.IMPORTANCE_LOW))
        val open=PendingIntent.getActivity(applicationContext,0,Intent(applicationContext,MainActivity::class.java),PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
        val notification=Notification.Builder(applicationContext,"transfers").setSmallIcon(app.actionbridge.R.drawable.ic_transfer).setContentTitle("ActionBridge").setContentText(message).setProgress(100,percent,percent==0).setOngoing(true).setContentIntent(open).addAction(Notification.Action.Builder(null,"Cancel",PendingIntent.getBroadcast(applicationContext,jobId.hashCode(),Intent(applicationContext,CancelReceiver::class.java).putExtra("id",jobId),PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)).build()).build()
        return if(Build.VERSION.SDK_INT>=29)ForegroundInfo(jobId.hashCode(),notification,ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC) else ForegroundInfo(jobId.hashCode(),notification)
    }
    override suspend fun doWork():Result = withContext(Dispatchers.IO) {
        val file=Queue.metadata(applicationContext,jobId)
        if(!file.exists())return@withContext Result.failure()
        try {
            val j=JSONObject(file.readText());if(j.optString("state")=="cancelled")return@withContext Result.failure()
            setForeground(info("Connecting to your PC",0))
            var peer=Peer.parse(j.getJSONObject("peer"));val store=PeerStore(applicationContext)
            val identity=store.get(peer.id)?:error("PC removed. Approve it again.")
            check(identity.optBoolean("approved")){"Approve this phone on the PC first."}
            check(identity.getString("fingerprint")==peer.fingerprint){"PC identity changed. Approve it again."}
            var api=BridgeApi(peer,identity.getString("token"),applicationContext)
            try {val hello=JSONObject(api.request("/hello"));check(hello.getString("id")==peer.id){"Different PC at this address."}}
            catch(e:IOException) {
                val discovered=Discovery(applicationContext).scan().firstOrNull{it.id==peer.id&&it.fingerprint==peer.fingerprint}?:throw e
                peer=discovered;store.save(peer,identity.getString("token"),true);api=BridgeApi(peer,identity.getString("token"),applicationContext)
                check(JSONObject(api.request("/hello")).getString("id")==peer.id){"Different PC at this address."}
            }
            val request=j.getJSONObject("request");var remote=JSONObject(api.request("/jobs","POST",request))
            if(remote.getString("state")=="uploading" && request.getString("action") !in listOf("copy","url")) {
                val payload=File(Queue.directory(applicationContext),"$jobId.payload")
                check(payload.length()==request.getLong("size")){"Local file is missing. Share it again."}
                RandomAccessFile(payload,"r").use {source->
                    var offset=remote.getLong("offset");check(offset in 0..source.length()){ "Invalid PC upload offset." };source.seek(offset)
                    while(offset<source.length()) {
                        ensureActive();if(isStopped)throw CancellationException()
                        val count=minOf(4*1024*1024L,source.length()-offset).toInt();val chunk=ByteArray(count);source.readFully(chunk)
                        remote=JSONObject(api.request("/jobs/$jobId/content?offset=$offset","PUT",bytes=chunk));offset=remote.getLong("offset")
                        val percent=if(source.length()==0L)100 else (offset*100/source.length()).toInt()
                        Queue.state(applicationContext,jobId,"sending","Sending to ${peer.name}",percent);setForeground(info("Sending · $percent%",percent))
                    }
                }
            }
            remote=JSONObject(api.request("/jobs/$jobId/finish","POST",JSONObject()))
            var polls=0
            while(remote.getString("state") in listOf("queued","running") && polls++<120){delay(1000);ensureActive();remote=JSONObject(api.request("/jobs/$jobId"))}
            val state=remote.getString("state");val message=remote.optString("message","Waiting for PC action")
            Queue.state(applicationContext,jobId,state,message,100)
            if(state in listOf("queued","running"))return@withContext Result.retry()
            if(state in listOf("completed","submitted")){File(Queue.directory(applicationContext),"$jobId.payload").delete();return@withContext Result.success()}
            Result.failure(workDataOf("error" to message.take(1000)))
        }catch(e:CancellationException){if(runCatching{JSONObject(file.readText()).optString("state")}.getOrNull()!="cancelled")Queue.state(applicationContext,jobId,"paused","Transfer stopped. Tap Retry to resume.");throw e}
        catch(e:Exception) {
            if(runCatching{JSONObject(file.readText()).optString("state")}.getOrNull()=="cancelled")return@withContext Result.failure()
            val retry=e is IOException && (e !is ApiException || e.code in listOf(408,409,429,500,502,503,504)) && runAttemptCount<5
            Queue.state(applicationContext,jobId,if(retry)"retrying" else "failed",e.message?:"Could not send. Check the PC and Wi-Fi.")
            if(retry)Result.retry() else Result.failure(workDataOf("error" to (e.message?:"Transfer failed").take(1000)))
        }
    }
}
