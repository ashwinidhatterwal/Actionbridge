package app.actionbridge
import org.junit.Test
import org.junit.Assert.*
class ConnectionListTest {
 private val nearby=Peer("same-pc","My PC","192.168.1.10",45833,"pin")
 private val remote=nearby.copy(host="")
 @Test fun offlineDiscoveryKeepsBothSavedRoutes(){assertEquals(listOf(remote),ConnectionList.merge(listOf(nearby),listOf(remote),emptyList()))}
 @Test fun nearbyAndInternetAreOneComputer(){assertEquals(listOf(nearby),ConnectionList.merge(listOf(nearby),listOf(remote),listOf(nearby)))}
 @Test fun otherSavedComputerRemainsWhenOnlyOneIsFound(){val other=remote.copy(id="other-pc",name="Office");assertEquals(listOf(nearby,other),ConnectionList.merge(listOf(nearby),listOf(remote,other),listOf(nearby)))}
}
