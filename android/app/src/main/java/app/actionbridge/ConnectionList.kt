package app.actionbridge
/** Saved entries survive absence from discovery; an available LAN route is preferred. */
object ConnectionList {
 fun merge(savedNearby:List<Peer>,savedRemote:List<Peer>,nearby:List<Peer>):List<Peer>{
  val result=linkedMapOf<String,Peer>()
  savedNearby.forEach{result[it.id]=it};savedRemote.forEach{result[it.id]=it};nearby.forEach{result[it.id]=it}
  return result.values.toList()
 }
}
