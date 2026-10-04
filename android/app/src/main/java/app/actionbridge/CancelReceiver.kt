package app.actionbridge
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
class CancelReceiver:BroadcastReceiver() {
    override fun onReceive(context:Context,intent:Intent) {
        val id=intent.getStringExtra("id")?:return
        if(runCatching{java.util.UUID.fromString(id)}.isFailure)return
        Queue.cancel(context.applicationContext,id)
    }
}
