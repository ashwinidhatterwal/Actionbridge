package app.actionbridge
import org.junit.Assert.*
import org.junit.Test
import java.net.InetAddress
class NearbyPhoneTest {
 @Test fun onlyPrivateSourcesAreAccepted(){
  for(host in listOf("127.0.0.1","192.168.1.4","10.0.0.2","172.16.1.2","169.254.2.3","::1","fd00::1"))assertTrue(host,NearbyPhone.privateAddress(InetAddress.getByName(host)))
  for(host in listOf("8.8.8.8","172.15.1.2","172.32.1.2","192.0.2.1","2001:4860:4860::8888"))assertFalse(host,NearbyPhone.privateAddress(InetAddress.getByName(host)))
 }
}
