package app.actionbridge
import org.json.JSONObject
import org.junit.Test
import org.junit.Assert.*
class IncomingRulesTest {
 private fun file()=JSONObject().put("id","f58e42a4-7c38-4353-8272-c1f6c2269c24").put("name","invoice.pdf").put("kind","file").put("size",1234).put("sha256","a".repeat(64))
 @Test fun validFileAndZeroByteFile(){IncomingRules.validate(file());IncomingRules.validate(file().put("size",0))}
 @Test fun invalidMetadataRejected(){for(j in listOf(file().put("id","../../file"),file().put("size",2147483649L),file().put("size",-1),file().put("sha256","bad"),file().put("kind","shell"))){assertThrows(IllegalArgumentException::class.java){IncomingRules.validate(j)}}}
 @Test fun linksAndUnicodeBytesBounded(){val j=file().put("kind","link").put("size",0).put("text","https://example.com");IncomingRules.validate(j);assertThrows(IllegalArgumentException::class.java){IncomingRules.validate(j.put("text","javascript:alert(1)"))};assertThrows(IllegalArgumentException::class.java){IncomingRules.validate(j.put("kind","text").put("text","क".repeat(3000)))}}
 @Test fun receivedNamesCannotTraverse(){assertEquals("invoice.pdf",IncomingRules.safeName("../../invoice.pdf"));assertEquals("invoice.pdf",IncomingRules.safeName("C:\\folder\\invoice.pdf"));assertEquals("file",IncomingRules.safeName(""))}
}
