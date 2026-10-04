package app.actionbridge
import android.app.Activity
import android.content.Intent
import android.os.Bundle
class TextSelectionActivity:Activity(){
 override fun onCreate(state:Bundle?){super.onCreate(state);val text=intent.getCharSequenceExtra(Intent.EXTRA_PROCESS_TEXT)?.toString()
  if(!text.isNullOrBlank()&&text.length<=65536)startActivity(Intent(this,MainActivity::class.java).setAction("app.actionbridge.SELECTION").putExtra(Intent.EXTRA_TEXT,text).addFlags(Intent.FLAG_ACTIVITY_CLEAR_TOP))
  setResult(RESULT_CANCELED);finish()
 }
}
