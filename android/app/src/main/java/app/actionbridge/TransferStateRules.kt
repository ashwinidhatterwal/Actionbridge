package app.actionbridge
object TransferStateRules {
    // Cancellation must survive late progress/retry callbacks. Confirmed PC
    // outcomes may still arrive for an action already started before Cancel.
    fun ignoreUpdate(current:String,next:String)=current=="cancelled" && next in setOf("queued","sending","retrying","paused","running","failed")
    fun releasePayload(state:String)=state in setOf("completed","submitted")
}
