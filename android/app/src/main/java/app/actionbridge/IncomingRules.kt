package app.actionbridge
import org.json.JSONObject
import java.util.UUID
object IncomingRules {
 fun validate(j:JSONObject){
  val id=j.getString("id");require(UUID.fromString(id).toString()==id)
  val kind=j.getString("kind");require(kind in listOf("file","text","link")&&j.getLong("size") in 0..2147483648L)
  require(j.getString("name").length<=512)
  if(kind=="file")require(j.getString("sha256").matches(Regex("[a-f0-9]{64}")))
  else {require(j.getLong("size")==0L&&j.optString("text").isNotEmpty()&&j.getString("text").toByteArray(Charsets.UTF_8).size<=8192);if(kind=="link")require(Rules.webLink(j.getString("text")))}
 }
 fun safeName(name:String)=name.replace('\\','/').substringAfterLast('/').filter{it>=' '}.take(120).ifBlank{"file"}
}
