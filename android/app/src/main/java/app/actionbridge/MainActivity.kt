package app.actionbridge
import android.Manifest
import android.app.Activity
import android.app.AlertDialog
import android.content.Intent
import android.content.pm.PackageManager
import android.graphics.Color
import android.graphics.Typeface
import android.graphics.drawable.GradientDrawable
import android.net.Uri
import android.os.*
import android.view.*
import android.widget.*
import kotlinx.coroutines.*
import org.json.JSONObject
import org.json.JSONArray
import java.security.SecureRandom
import com.google.zxing.integration.android.IntentIntegrator
import java.net.URI

class MainActivity:Activity() {
    private val scope=CoroutineScope(SupervisorJob()+Dispatchers.Main)
    private val selected=mutableListOf<Uri>()
    private var peers=listOf<Peer>()
    private var printerData=JSONArray()
    private lateinit var content:LinearLayout
    private lateinit var pc:Spinner
    private lateinit var status:TextView
    private lateinit var fileLabel:TextView
    private lateinit var input:EditText
    private lateinit var chooseFiles:Button
    private lateinit var actionPicker:Spinner
    private val actions=listOf("save","open","print","copy","url")
    private lateinit var printPanel:LinearLayout
    private lateinit var printer:Spinner
    private lateinit var paper:Spinner
    private lateinit var copies:EditText
    private lateinit var landscape:CheckBox
    private lateinit var color:CheckBox
    private lateinit var fit:Spinner
    private lateinit var duplex:Spinner
    private lateinit var pageRange:EditText
    private lateinit var collate:CheckBox
    private lateinit var send:Button
    private lateinit var history:LinearLayout
    private var action="save"
    private lateinit var remoteEngine:RemoteEngine
    private var remoteReady=false
    private var discovered=setOf<String>()
    private var scanning=false
    private var busy=false
    private var selectionPending=false
    private var receiving=false
    private var nextReceiveAttempt=0L
    private var receiveHistory=""
    private lateinit var inbox:LinearLayout
    private var historyValue=""
    private lateinit var pages:List<ScrollView>
    private lateinit var navigation:List<Button>
    private lateinit var modeRow:RadioGroup
    private lateinit var fileActions:RadioGroup
    private lateinit var fileOptions:LinearLayout
    private lateinit var clearFiles:Button
    private lateinit var paste:Button
    private lateinit var outcome:TextView
    private var renderingAction=false
    private var activePeerKey=""
    private var pageIndex=0
    private fun radio(label:String)=RadioButton(this).apply{text=label;id=View.generateViewId();buttonTintList=android.content.res.ColorStateList.valueOf(teal);minHeight=dp(48);setTextColor(teal)}
    private fun showPage(index:Int){pageIndex=index;pages.forEachIndexed{i,page->page.visibility=if(i==index)View.VISIBLE else View.GONE};navigation.forEachIndexed{i,b->b.setTextColor(if(i==index)teal else Color.rgb(91,105,115));b.setTypeface(null,if(i==index)Typeface.BOLD else Typeface.NORMAL)}}
    private fun renderAction(){
        if(!::modeRow.isInitialized)return
        renderingAction=true
        val isText=action in listOf("copy","url")
        modeRow.check(modeRow.getChildAt(if(action=="copy")1 else if(action=="url")2 else 0).id)
        fileActions.check(fileActions.getChildAt(actions.indexOf(action).coerceIn(0,2)).id)
        renderingAction=false
        fileOptions.visibility=if(isText)View.GONE else View.VISIBLE
        input.visibility=if(isText)View.VISIBLE else View.GONE;paste.visibility=input.visibility
        input.hint=if(action=="url")"https://example.com" else "Type or paste text to copy"
        if(::printPanel.isInitialized)printPanel.visibility=if(action=="print")View.VISIBLE else View.GONE
        outcome.text=when(action){"print"->"Prints the selected PDF or image on your computer.";"copy"->"Copies this text to your computer’s clipboard. Then paste it in any app.";"url"->"Opens this link in the computer’s browser.";"open"->"Saves the file, then opens it on your computer.";else->"Saves files in Downloads / ActionBridge on your computer."}
        if(::send.isInitialized){send.text=when(action){"print"->"Send to printer";"copy"->"Copy text on computer";"url"->"Open link on computer";"open"->"Send & open file";else->"Send files"};send.isEnabled=!busy&&selectedPeer()!=null}
    }
    private fun chooseAction(value:String){val changed=action!=value;action=value;actionPicker.setSelection(actions.indexOf(value));renderAction();if(changed&&value=="print"&&selectedPeer()!=null&&!busy&&::remoteEngine.isInitialized)loadPrinters()}
    private fun manageComputer(){selectedPeer()?.let{peer->AlertDialog.Builder(this).setTitle(peer.name).setItems(arrayOf("Find nearby again","Remove saved computer")){_,which->if(which==0)scan(true) else AlertDialog.Builder(this).setMessage("Remove ${peer.name}? Your received files stay on this phone.").setPositiveButton("Remove"){_,_->PeerStore(this).forget(peer.id);remoteEngine.disconnect();activePeerKey="";scan(true)}.setNegativeButton("Cancel",null).show()}.show()}?:addComputer()}
    private fun friendlyState(value:String)=when(value){"queued"->"Waiting to send";"sending"->"Sending";"retrying"->"Waiting for computer";"paused"->"Paused · tap Retry";"failed"->"Needs attention";"uncertain"->"Check receiver / printer";"completed"->"Completed";"submitted"->"Sent to printer";"delivered"->"Delivered";"cancelled","canceled"->"Canceled";else->value.replaceFirstChar{it.uppercase()}}

    private val teal=Color.rgb(18,126,118)
    private fun dp(n:Int)=(resources.displayMetrics.density*n).toInt()
    private fun text(value:String,size:Float=15f,bold:Boolean=false)=TextView(this).apply{text=value;textSize=size;setTextColor(Color.rgb(29,43,57));if(bold)setTypeface(null,Typeface.BOLD);setPadding(0,dp(6),0,dp(6))}
    private fun column()=LinearLayout(this).apply{orientation=LinearLayout.VERTICAL}
    private fun card()=column().apply{setPadding(dp(16),dp(12),dp(16),dp(12));background=GradientDrawable().apply{setColor(Color.WHITE);cornerRadius=dp(20).toFloat();setStroke(dp(1),Color.rgb(226,233,237))};layoutParams=LinearLayout.LayoutParams(-1,-2).apply{bottomMargin=dp(14)}}
    private fun button(label:String,click:()->Unit)=Button(this).apply{text=label;isAllCaps=false;minHeight=dp(48);setTextColor(teal);backgroundTintList=android.content.res.ColorStateList.valueOf(Color.rgb(228,244,241));setOnClickListener{click()}}
    private fun spinner(values:List<String>)=Spinner(this).apply{adapter=ArrayAdapter(this@MainActivity,android.R.layout.simple_spinner_dropdown_item,values)}
    private fun fieldLabel(parent:LinearLayout,value:String){parent.addView(text(value,13f,true))}
    private fun about(){AlertDialog.Builder(this).setTitle("ActionBridge · 0.7.0").setMessage("Share files, text and links with your Windows or Ubuntu computer nearby or over the internet.\n\nLocal transfers stay on your network. Optional remote transfers use a connection service and an encrypted direct connection. No advertising or analytics. The PC needs to be awake and running ActionBridge.\n\nPrinting supports PDF and images. Submitted means accepted by the print system, not confirmed physical output.\n\nSource code includes the privacy policy, licenses and Windows companion setup.").setNegativeButton("Licenses"){_,_->val notices=TextView(this).apply{text=assets.open("notices.txt").bufferedReader().use{it.readText()};textSize=12f;setPadding(dp(20),dp(12),dp(20),dp(12))};AlertDialog.Builder(this).setTitle("Open-source licenses").setView(ScrollView(this).apply{addView(notices)}).setPositiveButton("Done",null).show()}.setNeutralButton("Privacy"){_,_->val policy=TextView(this).apply{text=assets.open("privacy.txt").bufferedReader().use{it.readText()};textSize=14f;setPadding(dp(20),dp(12),dp(20),dp(12))};val scroll=ScrollView(this).apply{addView(policy)};AlertDialog.Builder(this).setTitle("Privacy policy").setView(scroll).setPositiveButton("Done",null).show()}.setPositiveButton("Done",null).show()}
    private fun showFiles(){
        val names=selected.take(3).map{uri->runCatching{contentResolver.query(uri,arrayOf(android.provider.OpenableColumns.DISPLAY_NAME),null,null,null)?.use{cursor->if(cursor.moveToFirst())cursor.getString(0) else null}}.getOrNull()?:"Shared file"}
        clearFiles.visibility=if(selected.isEmpty())View.GONE else View.VISIBLE
        fileLabel.text=if(selected.isEmpty())"Choose a document, photo or any file." else names.joinToString("\n")+(if(selected.size>3)"\n+ ${selected.size-3} more files" else "")
    }
    private fun selectedPeer():Peer?=peers.getOrNull(pc.selectedItemPosition-1)
    override fun onCreate(state:Bundle?) {
        super.onCreate(state)
        val root=column().apply{setBackgroundColor(Color.rgb(244,247,250))};setContentView(root)
        root.setOnApplyWindowInsetsListener{view,insets->view.setPadding(insets.systemWindowInsetLeft,insets.systemWindowInsetTop,insets.systemWindowInsetRight,insets.systemWindowInsetBottom);insets}
        val brand=LinearLayout(this).apply{gravity=Gravity.CENTER_VERTICAL;setPadding(dp(20),dp(8),dp(16),dp(8))}
        brand.addView(ImageView(this).apply{setImageResource(R.drawable.ic_bridge);contentDescription="ActionBridge"},LinearLayout.LayoutParams(dp(36),dp(36)))
        brand.addView(text("ActionBridge",22f,true).apply{setPadding(dp(10),0,0,0)},LinearLayout.LayoutParams(0,-2,1f));brand.addView(button("+ Add"){addComputer()}.apply{contentDescription="Add a computer"});root.addView(brand)
        val frame=FrameLayout(this);root.addView(frame,LinearLayout.LayoutParams(-1,0,1f))
        val home=column().apply{setPadding(dp(20),dp(10),dp(20),dp(20))};content=home
        val activity=column().apply{setPadding(dp(20),dp(10),dp(20),dp(20))}
        val settings=column().apply{setPadding(dp(20),dp(10),dp(20),dp(20))}
        pages=listOf(home,activity,settings).map{body->ScrollView(this).apply{isFillViewport=true;addView(body);frame.addView(this,FrameLayout.LayoutParams(-1,-1))}}
        val nav=LinearLayout(this).apply{setPadding(dp(12),dp(4),dp(12),dp(4));setBackgroundColor(Color.WHITE)};root.addView(nav)
        navigation=listOf("Home","Activity","Settings").mapIndexed{i,label->button(label){showPage(i)}.apply{backgroundTintList=android.content.res.ColorStateList.valueOf(Color.WHITE);nav.addView(this,LinearLayout.LayoutParams(0,dp(52),1f))}}
        val connect=card();home.addView(connect);connect.addView(text("Send to",13f,true));pc=spinner(listOf("Choose a computer")).apply{minimumHeight=dp(48);contentDescription="Receiving computer"};connect.addView(pc)
        status=text("Add your computer once. It stays saved until you remove it.",13f);connect.addView(status);connect.addView(button("Manage this computer"){manageComputer()})
        val payload=card();home.addView(payload);payload.addView(text("What would you like to send?",19f,true))
        modeRow=RadioGroup(this).apply{orientation=RadioGroup.HORIZONTAL};listOf("Files","Text","Link").forEach{modeRow.addView(radio(it),RadioGroup.LayoutParams(0,-2,1f))};payload.addView(modeRow)
        actionPicker=spinner(listOf("Save files","Open file","Print file","Copy text","Open web link"));actionPicker.visibility=View.GONE;payload.addView(actionPicker)
        fileOptions=column();payload.addView(fileOptions);fileLabel=text("Choose a document, photo or any file.",14f);fileOptions.addView(fileLabel)
        chooseFiles=button("Choose files…"){startActivityForResult(Intent(Intent.ACTION_OPEN_DOCUMENT).apply{type="*/*";addCategory(Intent.CATEGORY_OPENABLE);putExtra(Intent.EXTRA_ALLOW_MULTIPLE,true)},41)};fileOptions.addView(chooseFiles)
        clearFiles=button("Clear selection"){selected.clear();showFiles()};fileOptions.addView(clearFiles);fileOptions.addView(text("On the computer",13f,true))
        fileActions=RadioGroup(this).apply{orientation=RadioGroup.HORIZONTAL};listOf("Save","Open","Print").forEach{fileActions.addView(radio(it),RadioGroup.LayoutParams(0,-2,1f))};fileOptions.addView(fileActions)
        input=EditText(this).apply{hint="Type or paste text";minLines=3;maxLines=8;setTextColor(Color.rgb(29,43,57));inputType=android.text.InputType.TYPE_CLASS_TEXT or android.text.InputType.TYPE_TEXT_FLAG_MULTI_LINE};payload.addView(input)
        paste=button("Paste from clipboard"){val clipboard=getSystemService(CLIPBOARD_SERVICE) as android.content.ClipboardManager;clipboard.primaryClip?.getItemAt(0)?.coerceToText(this)?.let{input.setText(it)}};payload.addView(paste)
        outcome=text("",13f);payload.addView(outcome)
        modeRow.setOnCheckedChangeListener{group,id->if(!renderingAction){val index=(0 until group.childCount).indexOfFirst{group.getChildAt(it).id==id};chooseAction(if(index==1)"copy" else if(index==2)"url" else if(action in listOf("copy","url"))"save" else action)}}
        fileActions.setOnCheckedChangeListener{group,id->if(!renderingAction){val index=(0 until group.childCount).indexOfFirst{group.getChildAt(it).id==id};if(index>=0)chooseAction(actions[index])}}
        actionPicker.onItemSelectedListener=object:AdapterView.OnItemSelectedListener{
            override fun onNothingSelected(p:AdapterView<*>?){}
            override fun onItemSelected(p:AdapterView<*>?,v:View?,position:Int,id:Long){val changed=action!=actions[position];action=actions[position];renderAction();if(changed&&action=="print"&&selectedPeer()!=null&&!busy&&::remoteEngine.isInitialized)loadPrinters()}
        }
        printPanel=card();printPanel.visibility=View.GONE;content.addView(printPanel);printPanel.addView(text("PRINT SETTINGS",13f,true));printPanel.addView(text("Choose how your document comes out.",14f));fieldLabel(printPanel,"Printer");printer=spinner(listOf("Default printer"));printPanel.addView(printer);fieldLabel(printPanel,"Paper size");paper=spinner(listOf("Default"));printPanel.addView(paper)
        fieldLabel(printPanel,"Number of copies");copies=EditText(this).apply{setText("1");hint="Copies (1–99)";inputType=android.text.InputType.TYPE_CLASS_NUMBER};printPanel.addView(copies)
        val advanced=column().apply{visibility=View.GONE}
        printPanel.addView(button("More print options"){advanced.visibility=if(advanced.visibility==View.GONE)View.VISIBLE else View.GONE})
        printPanel.addView(advanced)
        landscape=CheckBox(this).apply{text="Landscape"};color=CheckBox(this).apply{text="Color (if supported)";isChecked=true};advanced.addView(landscape);advanced.addView(color)
        fieldLabel(advanced,"Page scaling");fit=spinner(listOf("Fit to page","Fill page (crop edges)","Actual size"));advanced.addView(fit)
        fieldLabel(advanced,"Two-sided printing");duplex=spinner(listOf("Printer default","One-sided","Two-sided · long edge","Two-sided · short edge"));advanced.addView(duplex)
        fieldLabel(advanced,"PDF pages");pageRange=EditText(this).apply{hint="All pages, or a range such as 2-5";inputType=android.text.InputType.TYPE_CLASS_TEXT;isSingleLine=true};advanced.addView(pageRange)
        collate=CheckBox(this).apply{text="Collate copies (1,2,3 · 1,2,3)";isChecked=true};advanced.addView(collate)
        advanced.addView(text("Fit preserves the full page. Fill may crop edges. Two-sided options depend on your printer.",12f))
        advanced.addView(button("Refresh printers"){loadPrinters()})
        printer.onItemSelectedListener=object:AdapterView.OnItemSelectedListener {
            override fun onNothingSelected(p:AdapterView<*>?){}
            override fun onItemSelected(p:AdapterView<*>?,v:View?,position:Int,id:Long) {
                val j=if(position>0)printerData.optJSONObject(position-1) else (0 until printerData.length()).map{printerData.getJSONObject(it)}.firstOrNull{it.optBoolean("isDefault")};val papers=j?.optJSONArray("papers")
                paper.adapter=ArrayAdapter(this@MainActivity,android.R.layout.simple_spinner_dropdown_item,listOf("Default")+(0 until (papers?.length()?:0)).map{papers!!.getString(it)})
                color.isEnabled=j?.optBoolean("supportsColor",true)?:true;if(!color.isEnabled)color.isChecked=false
                duplex.isEnabled=j?.optBoolean("supportsDuplex",false)?:false;duplex.setSelection(0)
            }
        }
        send=button("Send files"){sendNow()};send.setTextColor(Color.WHITE);send.backgroundTintList=android.content.res.ColorStateList.valueOf(teal);content.addView(send,LinearLayout.LayoutParams(-1,dp(54)))
        content.addView(text("Your saved computers stay here, even when offline. Received items are in Activity.",12f))
        remoteEngine=RemoteEngine(this,{message->if(selectedPeer()?.host==""||busy)status.text=message},{ready->remoteReady=ready;if(ready&&action=="print"&&!busy)remoteEngine.printers()},{data->showPrinters(data)},{value->setBusy(value)})
        content.addView(remoteEngine.web,LinearLayout.LayoutParams(1,1));remoteEngine.web.visibility=View.INVISIBLE
        val old=getSharedPreferences("remote",MODE_PRIVATE);old.getString("code",null)?.let{runCatching{PeerStore(this).saveRemote(it);old.edit().clear().apply()}}

        activity.addView(text("Activity",25f,true));activity.addView(text("Sent and received items stay here.",14f))
        val activityMode=RadioGroup(this).apply{orientation=RadioGroup.HORIZONTAL};activityMode.addView(radio("Received"),RadioGroup.LayoutParams(0,-2,1f));activityMode.addView(radio("Sent"),RadioGroup.LayoutParams(0,-2,1f));activity.addView(activityMode)
        inbox=column();history=column();activity.addView(inbox);activity.addView(history);history.visibility=View.GONE
        activityMode.check(activityMode.getChildAt(0).id);activityMode.setOnCheckedChangeListener{group,id->val received=id==group.getChildAt(0).id;inbox.visibility=if(received)View.VISIBLE else View.GONE;history.visibility=if(received)View.GONE else View.VISIBLE}
        settings.addView(text("Settings",25f,true))
        val connections=card();settings.addView(connections);connections.addView(text("Your computers",19f,true));connections.addView(text("Add a computer once by scanning its QR code. Use the same Wi-Fi for nearby transfers. Your saved computer can also connect over the internet.",14f));connections.addView(button("Add a computer"){addComputer()});connections.addView(button("Find nearby computers"){scan(true);showPage(0)});connections.addView(button("Manage selected computer"){manageComputer()})
        val help=card();settings.addView(help);help.addView(text("Sending & receiving",19f,true));help.addView(text("Keep the computer awake with ActionBridge running. For internet transfers, keep this phone’s screen open while sending or receiving.\n\nFiles from a computer appear in Activity → Received. Text has a Copy button so you can paste it in any app.\n\nChoose Files → Print to see your computer’s printers and print options.",14f))
        val trouble=card();settings.addView(trouble);trouble.addView(text("Computer not connecting?",19f,true));trouble.addView(text("Open ActionBridge on the computer. Nearby transfers need the same network; guest Wi-Fi or a VPN may block discovery. For internet access, scan the computer’s current QR code. Your saved computer stays here while offline.",14f));trouble.addView(button("Privacy & licenses · 0.7.0"){about()})
        pc.onItemSelectedListener=object:AdapterView.OnItemSelectedListener{
            override fun onNothingSelected(p:AdapterView<*>?){}
            override fun onItemSelected(p:AdapterView<*>?,v:View?,pos:Int,id:Long){
                val peer=selectedPeer();val key=peer?.let{"${it.id}|${it.host}|${it.fingerprint}"}?:""
                if(key!=activePeerKey){activePeerKey=key;remoteReady=false;printerData=JSONArray();printer.adapter=ArrayAdapter(this@MainActivity,android.R.layout.simple_spinner_dropdown_item,listOf("Default printer"))
                    if(peer==null){PeerStore(this@MainActivity).setLast("");remoteEngine.disconnect();status.text="Select a saved computer or tap + Add."}else{PeerStore(this@MainActivity).setLast(peer.id);val code=PeerStore(this@MainActivity).remote(peer.id)?.getString("code");if(code!=null)remoteEngine.connect(code) else remoteEngine.disconnect();status.text=if(peer.host.isEmpty())"Connecting over the internet…" else if(peer.id in discovered)"Nearby · ${peer.name}" else "${peer.name} stays saved. It will receive when available.";if(action=="print"&&!busy)loadPrinters()}
                };renderAction()
            }
        }
        if(state!=null){state.getStringArrayList("files")?.forEach{selected.add(Uri.parse(it))};input.setText(state.getString("text",""));action=state.getString("action","save");actionPicker.setSelection(actions.indexOf(action).coerceAtLeast(0))}else consume(intent)
        showFiles();renderAction();showPage(state?.getInt("page",0)?:0)
        if(Build.VERSION.SDK_INT>=33&&checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS)!=PackageManager.PERMISSION_GRANTED)requestPermissions(arrayOf(Manifest.permission.POST_NOTIFICATIONS),51)
        Queue.cleanup(this);scan()
        scope.launch{while(isActive){refreshHistory();refreshIncoming();delay(2000)}}
        scope.launch{while(isActive){delay(3000);receiveNearby()}}
        scope.launch{while(isActive){delay(15000);if(!busy)scan()}}
    }
    private fun consume(intent:Intent) {
        if(intent.action=="app.actionbridge.SELECTION"){input.setText(intent.getCharSequenceExtra(Intent.EXTRA_TEXT)?.toString()?:"");action="copy";selectionPending=true}
        if(intent.action==Intent.ACTION_SEND){intent.getParcelableExtra<Uri>(Intent.EXTRA_STREAM)?.let{selected.add(it)};input.setText(intent.getCharSequenceExtra(Intent.EXTRA_TEXT)?.toString()?:"")}
        if(intent.action==Intent.ACTION_SEND_MULTIPLE)intent.getParcelableArrayListExtra<Uri>(Intent.EXTRA_STREAM)?.let{selected.addAll(it.take(20))}
        if(!selectionPending&&selected.isEmpty()&&input.text.isNotBlank())action=if(Rules.webLink(input.text.toString().trim()))"url" else "copy"
        // Set the action before rendering is complete through the matching radio.
        actionPicker.setSelection(actions.indexOf(action).coerceAtLeast(0))
    }
    override fun onSaveInstanceState(out:Bundle){out.putStringArrayList("files",ArrayList(selected.map{it.toString()}));out.putString("text",input.text.toString());out.putString("action",action);out.putInt("page",pageIndex);super.onSaveInstanceState(out)}
    override fun onActivityResult(request:Int,result:Int,data:Intent?){super.onActivityResult(request,result,data);val scan=IntentIntegrator.parseActivityResult(request,result,data);if(scan?.contents!=null){pairRemote(scan.contents);return};if(request==41&&result==RESULT_OK&&data!=null){selected.clear();data.clipData?.let{for(i in 0 until minOf(it.itemCount,20))selected.add(it.getItemAt(i).uri)}?:data.data?.let{selected.add(it)};selected.forEach{runCatching{contentResolver.takePersistableUriPermission(it,Intent.FLAG_GRANT_READ_URI_PERMISSION)}};showFiles()}}
    private fun addComputer(){AlertDialog.Builder(this).setTitle("Add your computer").setItems(arrayOf("Scan QR code on PC","Paste pairing code","Find nearby computers")){_,which->when(which){0->IntentIntegrator(this).setDesiredBarcodeFormats(IntentIntegrator.QR_CODE).setPrompt("On the computer: Add device → A phone → show QR code").setBeepEnabled(false).setOrientationLocked(false).initiateScan();1->{val entry=EditText(this).apply{hint="Paste the code from your PC"};AlertDialog.Builder(this).setTitle("Connect computer").setView(entry).setPositiveButton("Connect"){_,_->pairRemote(entry.text.toString())}.setNegativeButton("Cancel",null).show()};else->scan()}}.show()}
    private fun pairRemote(raw:String){try{
        val code=raw.trim();require(code.startsWith("abremote:")&&code.length<4096);val encoded=code.removePrefix("abremote:")
        val payload=JSONObject(String(android.util.Base64.decode(encoded,android.util.Base64.URL_SAFE or android.util.Base64.NO_WRAP or android.util.Base64.NO_PADDING),Charsets.UTF_8));val u=URI(payload.getString("service"))
        require(u.scheme=="https"&&u.host!=null&&u.userInfo==null&&u.query==null&&u.fragment==null&&(u.path.isNullOrEmpty()||u.path=="/"));require(payload.getString("room").matches(Regex("[a-f0-9]{32}")));require(payload.optString("fingerprint").isEmpty()||payload.optString("fingerprint").matches(Regex("[a-f0-9]{64}")));for(k in listOf("key","secret"))require(payload.getString(k).matches(Regex("[A-Za-z0-9_-]{43}")))
        AlertDialog.Builder(this).setTitle("Connect ${payload.optString("name","your PC")}?").setMessage("Allow this phone to save, open and print files, copy text, and open links on this PC. Scan only the code shown on your own computer.").setPositiveButton("Connect"){_,_->val peer=PeerStore(this).saveRemote(encoded);showPage(0);scan(selectId=peer.id)}.setNegativeButton("Cancel",null).show()
    }catch(e:Exception){status.text="Invalid code. In ActionBridge on your PC, choose Add device → A phone."}}
    private fun scan(manual:Boolean=false,selectId:String?=null){if(busy||scanning)return;scanning=true;scope.launch{try{val store=PeerStore(this@MainActivity);if(manual||peers.isEmpty())status.text="Finding nearby computers…";val found=try{Discovery(this@MainActivity).scan()}catch(e:Exception){emptyList()};if(busy)return@launch;discovered=found.map{it.id}.toSet()
        val previousPC=selectId?:selectedPeer()?.id?:store.last();val updated=ConnectionList.merge(store.remembered(),store.remotes(),found)
        val labels=listOf("Choose a computer")+updated.map{it.name+when{it.id in discovered->" · Nearby";it.host.isEmpty()->" · Internet";else->" · Saved"}}
        val currentLabels=(0 until pc.count).map{pc.getItemAtPosition(it).toString()};val identitiesChanged=updated.map{it.id}!=peers.map{it.id};peers=updated
        if(labels!=currentLabels||identitiesChanged||selectId!=null){pc.adapter=ArrayAdapter(this@MainActivity,android.R.layout.simple_spinner_dropdown_item,labels);pc.setSelection(peers.indexOfFirst{it.id==previousPC}.let{if(it<0)0 else it+1})}
        renderAction()
        if(peers.isEmpty())status.text="Tap Add computer, then scan the QR code shown on your PC."
        if(selectionPending){selectionPending=false;val last=previousPC;val index=peers.indexOfFirst{it.id==last};if(index>=0){pc.setSelection(index+1);action="copy";actionPicker.setSelection(actions.indexOf("copy"));sendNow()}else status.text="Choose and pair your PC, then tap Copy on PC."}
    }finally{scanning=false}}}
    private fun showPrinters(data:JSONArray){printerData=data;printer.adapter=ArrayAdapter(this,android.R.layout.simple_spinner_dropdown_item,listOf("Default printer")+(0 until data.length()).map{data.getJSONObject(it).getString("name")});status.text="Connected · ${data.length()} printer(s)"}
    private suspend fun waitRemote(peer:Peer){val code=PeerStore(this).remote(peer.id)?.getString("code")?:error("Pair this PC by scanning its QR code first.");remoteEngine.connect(code);withTimeout(35000){while(!remoteReady)delay(150)}}
    private fun retryRemote(j:JSONObject){if(busy)return;setBusy(true);scope.launch{try{val peer=Peer.parse(j.getJSONObject("peer"));waitRemote(peer);remoteEngine.send(listOf(j.getString("id")))}catch(e:Exception){status.text="PC unavailable. It stays saved; try again when online.";setBusy(false)}}}
    private suspend fun connected(peer:Peer):BridgeApi=withContext(Dispatchers.IO) {
        val store=PeerStore(this@MainActivity);val old=store.get(peer.id)
        val remotePin=store.remote(peer.id)?.optString("fingerprint")?:""
        check(remotePin.isEmpty()||remotePin==peer.fingerprint){"This nearby PC does not match your paired computer. Use the internet connection or scan the correct PC again."}
        check(old==null||old.getString("fingerprint")==peer.fingerprint){"PC identity changed. Forget this PC and approve it again."}
        val token=old?.getString("token")?:ByteArray(32).also{SecureRandom().nextBytes(it)}.joinToString(""){"%02x".format(it)}
        val api=BridgeApi(peer,token,this@MainActivity)
        check(JSONObject(api.hello()).getString("id")==peer.id){"PC identity mismatch."}
        if(old==null||!old.optBoolean("approved")){
            store.save(peer,token,false);withContext(Dispatchers.Main){status.text="Tap Allow on ${peer.name} to connect this phone."}
            api.request("/pair","POST",JSONObject().put("clientId",store.clientId()).put("name",Build.MODEL.take(80)).put("token",token));store.save(peer,token,true)
        } else {
            try{api.request("/status");store.save(peer,token,true)}catch(e:ApiException){if(e.code!=401)throw e;store.save(peer,token,false);withContext(Dispatchers.Main){status.text="Access removed. Tap Allow on the PC to reconnect."};api.request("/pair","POST",JSONObject().put("clientId",store.clientId()).put("name",Build.MODEL.take(80)).put("token",token));store.save(peer,token,true)}
        }
        // An approved LAN phone receives the same private remote capability as the QR.
        runCatching{val code=JSONObject(api.request("/connection")).optString("code");if(code.startsWith("abremote:"))store.saveRemote(code.removePrefix("abremote:"))}
        api
    }
    private fun setBusy(value:Boolean){busy=value;send.isEnabled=!value&&selectedPeer()!=null;pc.isEnabled=!value;chooseFiles.isEnabled=!value;clearFiles.isEnabled=!value;paste.isEnabled=!value;input.isEnabled=!value;for(group in listOf(modeRow,fileActions))for(i in 0 until group.childCount)group.getChildAt(i).isEnabled=!value;if(!value)window.clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)}
    private fun loadPrinters(){val peer=selectedPeer()?:return;if(busy)return;setBusy(true);scope.launch{try{if(peer.host.isEmpty()){waitRemote(peer);remoteEngine.printers()}else{try{val api=connected(peer);showPrinters(withContext(Dispatchers.IO){JSONArray(api.request("/printers"))})}catch(e:Exception){if(PeerStore(this@MainActivity).remote(peer.id)==null)throw e;waitRemote(peer);remoteEngine.printers()}}}catch(e:Exception){status.text=e.message?:"Could not load printers."}finally{setBusy(false)}}}
    private fun sendNow(){
        if(busy||receiving){status.text="Finishing a transfer. Try again in a moment.";return}
        var peer=selectedPeer()?:run{status.text="Tap Add computer to connect your PC.";return}
        val mode=action;val text=input.text.toString().trim();val files=selected.toList()
        if(mode in listOf("open","save","print")&&files.isEmpty()){status.text="Choose a file first.";return}
        if(mode in listOf("copy","url")&&(text.isEmpty()||text.length>65536)){status.text="Enter text up to 65,536 characters.";return}
        if(mode=="url"&&!Rules.webLink(text)){status.text="Enter a complete HTTP or HTTPS web link.";return}
        val number=copies.text.toString().toIntOrNull()?:0;if(mode=="print"&&number !in 1..99){status.text="Copies must be between 1 and 99.";return}
        val range=try{PrintOptions.pages(if(mode=="print")pageRange.text.toString() else "")}catch(e:IllegalArgumentException){status.text=e.message;return}
        val options=JSONObject().put("duplex",listOf("default","simplex","long","short")[duplex.selectedItemPosition.coerceIn(0,3)]).put("pageFrom",range.first).put("pageTo",range.second).put("collate",collate.isChecked).put("copies",if(mode=="print")number else 1).put("landscape",landscape.isChecked).put("color",color.isChecked).put("paper",paper.selectedItem?.toString()?:"Default").put("fit",listOf("fit","fill","actual")[fit.selectedItemPosition.coerceIn(0,2)])
        if(printer.selectedItemPosition>0)options.put("printer",printer.selectedItem.toString())
        setBusy(true)
        scope.launch{try{
            if(peer.host.isEmpty())waitRemote(peer) else try{connected(peer)}catch(e:Exception){
                if(PeerStore(this@MainActivity).remote(peer.id)==null)throw e
                peer=peer.copy(host="");waitRemote(peer)
            }
            val remoteMode=peer.host.isEmpty();val remoteIds=mutableListOf<String>()
            val sources=if(mode in listOf("copy","url"))listOf<Uri?>(null)else files
            sources.forEachIndexed{index,uri->
                status.text="Preparing ${index+1} of ${sources.size}…"
                withContext(NonCancellable+Dispatchers.IO){
                    val id=Queue.stage(applicationContext,uri,if(uri==null)text else null,peer,mode,options){bytes->if(bytes%(4*1024*1024)<65536)runOnUiThread{if(!isDestroyed)status.text="Preparing · ${bytes/1048576} MB"}}
                    if(remoteMode){val j=JSONObject(Queue.metadata(applicationContext,id).readText());j.put("transport","remote").put("state","paused").put("message","Ready to send. Retry when your PC is connected.");Queue.write(Queue.metadata(applicationContext,id),j);remoteIds.add(id)}else Queue.enqueue(applicationContext,id)
                }
            }
            if(remoteMode){remoteEngine.send(remoteIds);window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)}
            status.text=if(remoteMode)"Sending to ${peer.name}. Keep this screen open." else "Sending to ${peer.name}. You can leave this screen.";selected.clear();showFiles();input.setText("");refreshHistory()
        }catch(e:Exception){status.text=e.message?:"Could not prepare transfer.";setBusy(false)}finally{if(peer.host.isNotEmpty())setBusy(false)}}
    }
    private suspend fun refreshHistory(){
        val records=withContext(Dispatchers.IO){Queue.list(this@MainActivity)};val value=records.toString();if(value==historyValue)return;historyValue=value;history.removeAllViews()
        if(records.isEmpty())history.addView(text("Your transfers will appear here.",13f))
        records.take(20).forEach{j->val view=card();val r=j.getJSONObject("request");view.addView(text(r.getString("name"),15f,true));view.addView(text("${friendlyState(j.optString("state"))} · ${j.optInt("progress")}%",13f));view.addView(ProgressBar(this,null,android.R.attr.progressBarStyleHorizontal).apply{max=100;progress=j.optInt("progress");progressTintList=android.content.res.ColorStateList.valueOf(teal)});view.addView(text(j.optString("message"),12f));val state=j.optString("state");val id=j.getString("id")
            if(state in listOf("paused","failed","retrying"))view.addView(button("Retry same transfer"){if(j.optString("transport")=="remote")retryRemote(j) else {Queue.enqueue(this,id);status.text="Retry queued."}})
            if(state in listOf("queued","sending","retrying","running"))view.addView(button("Cancel"){if(j.optString("transport")=="remote")remoteEngine.cancel(id) else Queue.cancel(this,id)})
            history.addView(view)
        }
    }
    private suspend fun receiveNearby(){
        if(busy||receiving||System.currentTimeMillis()<nextReceiveAttempt)return
        val peer=selectedPeer()?:return;if(peer.host.isEmpty())return
        val saved=PeerStore(this).get(peer.id)?:return;if(!saved.optBoolean("approved"))return
        receiving=true
        try{withContext(Dispatchers.IO){
            val api=BridgeApi(peer,saved.getString("token"),this@MainActivity);val incoming=Incoming(this@MainActivity)
            val items=JSONArray(api.request("/outbox"))
            for(i in 0 until items.length()){
                val item=items.getJSONObject(i);val id=item.getString("id");try{val start=incoming.begin(item.toString(),peer.id)
                if(start!="done"){
                    var offset=start.toLong();while(offset<item.getLong("size")){
                        ensureActive();val block=JSONObject(api.request("/outbox/$id?offset=$offset&count=262144"));check(block.getLong("offset")==offset);offset=incoming.append(id,offset,block.getString("data"))
                    };incoming.finish(id)
                };api.request("/outbox/$id/ack","POST",JSONObject())}catch(e:Exception){incoming.failure(id,e.message?:"Receiving interrupted");throw e}
            }
        }}catch(e:Exception){if(e is CancellationException)throw e;nextReceiveAttempt=System.currentTimeMillis()+30000
            if(selectedPeer()?.id==peer.id&&(e !is ApiException||e.code!=404)){PeerStore(this).remote(peer.id)?.getString("code")?.let{remoteEngine.connect(it)}}
        }finally{receiving=false}
    }
    private suspend fun refreshIncoming(){
        val records=withContext(Dispatchers.IO){Incoming(this@MainActivity).list()};val value=records.toString();if(value==receiveHistory)return;receiveHistory=value;inbox.removeAllViews()
        if(records.isEmpty())inbox.addView(text("Open this app to receive queued files and links from your PC.",13f))
        records.forEach{j->val view=card();view.addView(text(j.getString("name"),15f,true))
            if(!j.optBoolean("completed"))view.addView(text(j.optString("error").ifEmpty{"Receiving · will resume when connected"},13f))
            else if(j.getString("kind")=="file"){view.addView(text("Saved · "+if(Build.VERSION.SDK_INT>=29)"Downloads / ActionBridge" else "ActionBridge received files",13f));view.addView(button("Open file"){runCatching{val uri=Uri.parse(j.getString("uri"));val type=android.webkit.MimeTypeMap.getSingleton().getMimeTypeFromExtension(j.getString("name").substringAfterLast('.',"").lowercase())?:"*/*";startActivity(Intent(Intent.ACTION_VIEW).setDataAndType(uri,type).addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION))}.onFailure{status.text="No app available to open this file."}})}
            else{view.addView(text(j.optString("text"),14f).apply{maxLines=6;ellipsize=android.text.TextUtils.TruncateAt.END});view.addView(button("Copy"){(getSystemService(CLIPBOARD_SERVICE) as android.content.ClipboardManager).setPrimaryClip(android.content.ClipData.newPlainText("From PC",j.getString("text")));status.text="Copied. Paste it in any app.";Toast.makeText(this,"Copied to clipboard",Toast.LENGTH_SHORT).show()});if(j.getString("kind")=="link")view.addView(button("Open link"){startActivity(Intent(Intent.ACTION_VIEW,Uri.parse(j.getString("text"))))})}
            inbox.addView(view)
        }
    }
    override fun onDestroy(){scope.cancel();if(::remoteEngine.isInitialized)remoteEngine.destroy();super.onDestroy()}
}
