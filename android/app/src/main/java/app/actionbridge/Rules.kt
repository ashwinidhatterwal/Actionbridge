package app.actionbridge
object Rules {
    fun webLink(value:String):Boolean = runCatching {
        if(value.length !in 1..65536 || value.any{it.isWhitespace()})return false
        val uri=java.net.URI(value)
        uri.scheme in listOf("http","https") && !uri.host.isNullOrBlank() && uri.userInfo==null
    }.getOrDefault(false)
}
