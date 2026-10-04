package app.actionbridge
import android.content.ContextWrapper
import org.json.JSONObject
import org.junit.Test
import org.junit.Assert.*
import java.io.File
import java.nio.file.Files
import java.util.UUID
import java.util.Base64
import java.util.concurrent.CountDownLatch
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicInteger
class StorageAuditTest {
 private fun fixture(run:(ContextWrapper,File)->Unit){val root=Files.createTempDirectory("ab-storage-").toFile();val context=object:ContextWrapper(null){override fun getFilesDir()=root};try{run(context,root)}finally{root.deleteRecursively()}}
 @Test fun cancellationSurvivesLateProgress(){fixture{c,_->val id=UUID.randomUUID().toString();Queue.write(Queue.metadata(c,id),JSONObject().put("id",id).put("state","cancelled"));Queue.state(c,id,"sending","late progress",50);assertEquals("cancelled",JSONObject(Queue.metadata(c,id).readText()).getString("state"));Queue.state(c,id,"paused","late disconnect");assertEquals("cancelled",JSONObject(Queue.metadata(c,id).readText()).getString("state"))}}
 @Test fun confirmedRemoteSuccessReleasesPayloadAfterJournal(){fixture{c,_->val id=UUID.randomUUID().toString();Queue.write(Queue.metadata(c,id),JSONObject().put("id",id).put("state","sending"));val payload=File(Queue.directory(c),"$id.payload");payload.writeText("body");Queue.state(c,id,"submitted","accepted",100);assertFalse(payload.exists());assertEquals("submitted",JSONObject(Queue.metadata(c,id).readText()).getString("state"))}}
 @Test fun failedSuccessJournalDoesNotDeleteResumableBody(){fixture{c,_->val id=UUID.randomUUID().toString();val meta=Queue.metadata(c,id);Queue.write(meta,JSONObject().put("id",id).put("state","sending"));val payload=File(Queue.directory(c),"$id.payload");payload.writeText("body");File(meta.path+".new").mkdir();assertTrue(runCatching{Queue.state(c,id,"completed","done",100)}.isFailure);assertTrue(payload.exists());assertEquals("sending",JSONObject(meta.readText()).getString("state"))}}
 @Test fun confirmedPcOutcomeCanArriveAfterCancellation(){assertFalse(TransferStateRules.ignoreUpdate("cancelled","submitted"));assertFalse(TransferStateRules.ignoreUpdate("cancelled","uncertain"));assertTrue(TransferStateRules.ignoreUpdate("cancelled","retrying"))}
 @Test fun inboxOwnershipIsSharedBetweenInstances(){fixture{c,_->val j=JSONObject().put("id",UUID.randomUUID().toString()).put("kind","text").put("name","Text").put("size",0).put("text","Hello");val first=Incoming(c);val second=Incoming(c);assertEquals("0",first.begin(j.toString(),"pc-a"));assertTrue(runCatching{second.begin(j.toString(),"pc-b")}.isFailure);assertTrue(first.finish(j.getString("id")));assertEquals("done",second.begin(j.toString(),"pc-a"));assertFalse(second.finish(j.getString("id")))}}
 @Test fun simultaneousInboxWritesCommitOnlyOneChunk(){fixture{c,root->val id=UUID.randomUUID().toString();val size=262144;val j=JSONObject().put("id",id).put("kind","file").put("name","test.txt").put("size",size).put("sha256","a".repeat(64));Incoming(c).begin(j.toString(),"pc");val pool=Executors.newFixedThreadPool(12);val ready=CountDownLatch(12);val start=CountDownLatch(1);val done=CountDownLatch(12);val successes=AtomicInteger();val data=Base64.getEncoder().encodeToString(ByteArray(size){7});repeat(12){pool.submit{val store=Incoming(c);ready.countDown();start.await();if(runCatching{store.append(id,0,data)}.isSuccess)successes.incrementAndGet();done.countDown()}};assertTrue(ready.await(5,TimeUnit.SECONDS));start.countDown();assertTrue(done.await(10,TimeUnit.SECONDS));pool.shutdownNow();assertEquals(1,successes.get());assertEquals(size.toLong(),File(root,"inbox/$id.payload").length())}}
}
