package app.actionbridge
import org.junit.Assert.*
import org.junit.Test
class PrintOptionsTest {
 @Test fun range(){assertEquals(0 to 0,PrintOptions.pages(""));assertEquals(2 to 5,PrintOptions.pages("2 - 5"));assertEquals(3 to 3,PrintOptions.pages("3"))}
 @Test fun invalid(){for(value in listOf("0","5-2","1-501","a","1,3","999999999999")){try{PrintOptions.pages(value);fail(value)}catch(expected:IllegalArgumentException){}}}
}
