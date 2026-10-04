package app.actionbridge
import org.junit.Test
import org.junit.Assert.*
class ProtocolTest {
    @Test fun peerRoundTripPreservesPinAndAddress(){val peer=Peer("f58e42a4-7c38-4353-8272-c1f6c2269c24","Studio PC","192.168.1.8",45833,"a".repeat(64));assertEquals(peer,Peer.parse(peer.json()))}
    @Test fun signedByteIpv4AddressIsCorrect(){assertEquals(0xc0a80108L,LanNetwork.number(byteArrayOf(192.toByte(),168.toByte(),1,8)))}
    @Test fun subnetMatchesOnlyItsOwnAddresses(){assertTrue(LanNetwork.sameSubnet(0xc0a80108,0xc0a801ff,24));assertFalse(LanNetwork.sameSubnet(0xc0a80108,0xc0a80208,24))}
    @Test fun invalidSubnetPrefixDoesNotMatch(){assertFalse(LanNetwork.sameSubnet(1,1,0));assertFalse(LanNetwork.sameSubnet(1,1,33))}
    @Test fun linksAcceptOnlyRealWebUrls(){assertTrue(Rules.webLink("https://example.com/invoice?id=1"));assertFalse(Rules.webLink("file:///C:/Windows"));assertFalse(Rules.webLink("javascript:alert(1)"));assertFalse(Rules.webLink("https://user:password@example.com"));assertFalse(Rules.webLink("https://"));assertFalse(Rules.webLink("https://example.com title"))}
}
