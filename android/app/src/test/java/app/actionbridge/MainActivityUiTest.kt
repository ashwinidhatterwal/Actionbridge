package app.actionbridge

import android.content.Intent
import android.view.View
import android.widget.*
import org.junit.After
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.Robolectric
import org.robolectric.android.controller.ActivityController
import org.robolectric.annotation.Config

@RunWith(org.robolectric.RobolectricTestRunner::class)
@Config(sdk=[28])
class MainActivityUiTest {
    private var controller:ActivityController<MainActivity>?=null
    private fun launch(intent:Intent?=null):MainActivity {
        val c=if(intent==null)Robolectric.buildActivity(MainActivity::class.java) else Robolectric.buildActivity(MainActivity::class.java,intent)
        controller=c;c.setup();return c.get()
    }
    @Suppress("UNCHECKED_CAST")
    private fun <T> field(a:MainActivity,name:String):T=MainActivity::class.java.getDeclaredField(name).apply{isAccessible=true}.get(a) as T
    @After fun close(){controller?.pause()?.stop()?.destroy()}

    @Test fun newUserCannotSendToAnUnchosenComputer(){
        val a=launch()
        assertFalse(field<Button>(a,"send").isEnabled)
        assertEquals("save",field<String>(a,"action"))
        assertEquals(View.GONE,field<LinearLayout>(a,"printPanel").visibility)
        val pages=field<List<ScrollView>>(a,"pages")
        assertEquals(listOf(View.VISIBLE,View.GONE,View.GONE),pages.map{it.visibility})
    }
    @Test fun fileTextLinkAndPrintControlsMatchTheChosenTask(){
        val a=launch();val modes=field<RadioGroup>(a,"modeRow")
        modes.check(modes.getChildAt(1).id)
        assertEquals("copy",field<String>(a,"action"))
        assertEquals(View.VISIBLE,field<EditText>(a,"input").visibility)
        assertEquals(View.GONE,field<LinearLayout>(a,"fileOptions").visibility)
        modes.check(modes.getChildAt(2).id)
        assertEquals("url",field<String>(a,"action"))
        modes.check(modes.getChildAt(0).id)
        val choices=field<RadioGroup>(a,"fileActions");choices.check(choices.getChildAt(2).id)
        assertEquals("print",field<String>(a,"action"))
        assertEquals(View.VISIBLE,field<LinearLayout>(a,"printPanel").visibility)
        choices.check(choices.getChildAt(0).id)
        assertEquals(View.GONE,field<LinearLayout>(a,"printPanel").visibility)
    }
    @Test fun androidShareChoosesTheRightTaskAndNavigationKeepsTheDraft(){
        val a=launch(Intent(Intent.ACTION_SEND).setType("text/plain").putExtra(Intent.EXTRA_TEXT,"https://example.com"))
        assertEquals("url",field<String>(a,"action"))
        val nav=field<List<Button>>(a,"navigation");nav[1].performClick();nav[2].performClick();nav[0].performClick()
        assertEquals("https://example.com",field<EditText>(a,"input").text.toString())
        assertEquals("url",field<String>(a,"action"))
    }
    @Test fun preparationLocksThePayloadAndDestinationControls(){
        val a=launch();MainActivity::class.java.getDeclaredMethod("setBusy",Boolean::class.javaPrimitiveType).apply{isAccessible=true}.invoke(a,true)
        assertFalse(field<Spinner>(a,"pc").isEnabled)
        assertFalse(field<Button>(a,"chooseFiles").isEnabled)
        assertFalse(field<EditText>(a,"input").isEnabled)
        val modes=field<RadioGroup>(a,"modeRow")
        assertTrue((0 until modes.childCount).all{!modes.getChildAt(it).isEnabled})
    }
}
