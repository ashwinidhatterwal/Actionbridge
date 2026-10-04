package app.actionbridge
import android.app.Activity
import android.util.Base64
import android.webkit.*
import androidx.webkit.WebViewAssetLoader
import org.json.JSONArray
import org.json.JSONObject
import java.io.RandomAccessFile

/** Bundled WebRTC transport. All controls and history belong to the native main page. */
class RemoteEngine(private val activity:Activity,private val onStatus:(String)->Unit,private val onReady:(Boolean)->Unit,private val onPrinters:(JSONArray)->Unit,private val onBusy:(Boolean)->Unit) {
 val web=WebView(activity)
 private var loaded=false
 @Volatile private var connected=false
 private var code:String?=null
 private val allowed=java.util.concurrent.ConcurrentHashMap.newKeySet<String>()
 private var active=emptyList<String>()
 private var lastProgress=mutableMapOf<String,Int>()
 init {
  web.settings.javaScriptEnabled=true;web.settings.allowFileAccess=false;web.settings.allowContentAccess=false;web.settings.domStorageEnabled=false
  web.settings.mixedContentMode=WebSettings.MIXED_CONTENT_NEVER_ALLOW
  val loader=WebViewAssetLoader.Builder().addPathHandler("/assets/",WebViewAssetLoader.AssetsPathHandler(activity)).build()
  web.webViewClient=object:WebViewClient(){override fun shouldInterceptRequest(v:WebView,r:WebResourceRequest)=loader.shouldInterceptRequest(r.url)
   override fun shouldOverrideUrlLoading(v:WebView,r:WebResourceRequest)=true
   override fun onPageFinished(v:WebView,url:String){if(url.startsWith("https://appassets.androidplatform.net/")){loaded=true;val initial=code;code=null;initial?.let{connect(it)}}}
   override fun onReceivedSslError(v:WebView,h:SslErrorHandler,e:android.net.http.SslError){h.cancel()}}
  web.addJavascriptInterface(Callbacks(),"NativeBridge")
  web.loadUrl("https://appassets.androidplatform.net/assets/remote/index.html")
 }
 fun connect(value:String){if(code==value&&loaded){onReady(connected);if(!connected)js("ActionBridge.ensureConnected()");return};connected=false;onReady(false);code=value;if(loaded)js("ActionBridge.connect(${JSONObject.quote(value)})")}
 fun reconnect(){js("ActionBridge.reconnect()")}
 fun disconnect(){code=null;connected=false;js("ActionBridge.dispose()");onReady(false)}
 fun printers(){js("ActionBridge.printers()")}
 fun send(ids:List<String>){allowed.addAll(ids);active=ids;val jobs=JSONArray();ids.forEach{jobs.put(JSONObject(Queue.metadata(activity,it).readText()).getJSONObject("request"))};js("ActionBridge.send($jobs)")}
 fun cancel(id:String){js("ActionBridge.cancel(${JSONObject.quote(id)})")}
 private fun js(value:String){activity.runOnUiThread{web.evaluateJavascript(value,null)}}
 fun destroy(){active.forEach{runCatching{val j=JSONObject(Queue.metadata(activity,it).readText());if(j.optString("state") in setOf("sending","running","queued"))Queue.state(activity,it,"paused","Screen closed. Retry this transfer to check its status and resume.",j.optInt("progress"))}};web.removeJavascriptInterface("NativeBridge");web.stopLoading();web.destroy()}
 private val incoming=Incoming(activity)
 inner class Callbacks {
  @JavascriptInterface fun incomingBegin(value:String,pc:String):String=runCatching{incoming.begin(value,pc)}.getOrDefault("error")
  @JavascriptInterface fun incomingAppend(id:String,offset:Long,data:String):String=runCatching{incoming.append(id,offset,data).toString()}.onFailure{incoming.failure(id,it.message?:"Could not write received file")}.getOrDefault("error")
  @JavascriptInterface fun incomingFinish(id:String):Boolean=runCatching{incoming.finish(id);true}.onFailure{incoming.failure(id,it.message?:"Could not save received file")}.getOrDefault(false)

  @JavascriptInterface fun status(value:String){activity.runOnUiThread{onStatus(value.take(300))}}
  @JavascriptInterface fun ready(value:Boolean){connected=value;activity.runOnUiThread{onReady(value)}}
  @JavascriptInterface fun busy(value:Boolean){activity.runOnUiThread{onBusy(value)}}
  @JavascriptInterface fun printers(value:String){runCatching{val data=JSONArray(value);activity.runOnUiThread{onPrinters(data)}}}
  @JavascriptInterface fun job(id:String,state:String,message:String,progress:Int){if(id !in allowed)return
   if(state !in setOf("sending","running","paused","failed","completed","submitted","uncertain","cancelled"))return
   // Store progress by percentage rather than on every 32 KB chunk.
   synchronized(lastProgress){if(lastProgress[id]==progress&&state=="sending")return;lastProgress[id]=progress}
   Queue.state(activity,id,state,message.take(1000),progress)
  }
  @JavascriptInterface fun read(id:String,offset:Long,count:Int):String {if(id !in allowed||offset<0||count !in 1..32768)return "";return runCatching{RandomAccessFile(java.io.File(Queue.directory(activity),"$id.payload"),"r").use{f->require(offset+count<=f.length());f.seek(offset);val b=ByteArray(count);f.readFully(b);Base64.encodeToString(b,Base64.NO_WRAP)}}.getOrDefault("")}
 }
}
