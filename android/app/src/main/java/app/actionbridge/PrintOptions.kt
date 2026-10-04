package app.actionbridge
object PrintOptions {
    fun pages(value:String):Pair<Int,Int> {
        val text=value.trim()
        if(text.isEmpty())return 0 to 0
        val match=Regex("([0-9]+)(?:\\s*-\\s*([0-9]+))?").matchEntire(text)?:throw IllegalArgumentException("Use a PDF page number or range such as 2-5.")
        val start=match.groupValues[1].toIntOrNull()?:0
        val end=(match.groupValues[2].ifEmpty{match.groupValues[1]}).toIntOrNull()?:0
        require(start in 1..500&&end in start..500){"PDF pages must be between 1 and 500, in ascending order."}
        return start to end
    }
}
