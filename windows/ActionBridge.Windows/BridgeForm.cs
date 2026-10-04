using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ActionBridge.Core;
using Microsoft.Win32;
namespace ActionBridge.Windows;
public sealed class BridgeForm : Form {
    readonly string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ActionBridge");
    readonly string received=Path.Combine(KnownFolders.Downloads(),"ActionBridge");
    readonly Label status=new(){AutoSize=true,Text="Starting secure receiver…"};
    readonly ListView history=new(){Dock=DockStyle.Fill,View=View.Details,FullRowSelect=true};
    readonly Label emptyActivity=new(){Dock=DockStyle.Top,Height=70,Padding=new(18),Text="Ready for your next file\nConnect your phone, choose an action, and send. Your activity appears here.",ForeColor=Color.FromArgb(91,105,115)};
    readonly ListBox phones=new(){Dock=DockStyle.Fill};
    readonly NotifyIcon tray=new(){Visible=true,Text="ActionBridge",Icon=SystemIcons.Application};
    RemoteConnection? remote;
    readonly Label remoteStatus=new(){AutoSize=true,MaximumSize=new(650,0),Text="Internet connection · starting…",ForeColor=Color.FromArgb(91,105,115)};
    readonly System.Windows.Forms.Timer remoteRetry=new(){Interval=30000};readonly System.Windows.Forms.Timer activityTimer=new(){Interval=300};bool enrolling,historyDirty;
    BridgeServer? server;TrustStore? trust;Transfers? transfers;Outbox? outbox;readonly ComboBox sendTarget=new(){Width=360,DropDownStyle=ComboBoxStyle.DropDownList};readonly ListBox sent=new(){Dock=DockStyle.Fill};bool exiting;
    public BridgeForm() {
        Text="ActionBridge 0.5.0 · Phone ↔ PC";Size=new(1000,700);MinimumSize=new(800,560);Font=new("Segoe UI",10);Icon=Icon.ExtractAssociatedIcon(Environment.ProcessPath!)??SystemIcons.Application;tray.Icon=Icon;BackColor=Color.FromArgb(244,248,248);
        var header=new TableLayoutPanel{Dock=DockStyle.Top,Height=135,Padding=new(20),ColumnCount=2,RowCount=1};header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,190));var identity=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false};header.Controls.Add(identity,0,0);
        identity.Controls.Add(new Label{Text="ActionBridge",ForeColor=Color.FromArgb(18,126,118),Font=new("Segoe UI",19,FontStyle.Bold),AutoSize=true});identity.Controls.Add(status);identity.Controls.Add(remoteStatus);Controls.Add(header);
        var pairPhone=new Button{Text="＋ Connect phone",Width=170,Height=44,Anchor=AnchorStyles.Top|AnchorStyles.Right,FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(18,126,118),ForeColor=Color.White,Location=new(Width-220,28)};header.Controls.Add(pairPhone,1,0);pairPhone.Anchor=AnchorStyles.Right;identity.SizeChanged+=(s,e)=>remoteStatus.MaximumSize=new(Math.Max(200,identity.Width-15),0);pairPhone.Click+=async(s,e)=>{if(remote==null)return;try{await EnsureRemote(true);if(remote.Config!=null)remote.ShowPairing(this);}catch(Exception ex){MessageBox.Show(this,ex.Message,"Connect phone");}};
        var tabs=new TabControl{Dock=DockStyle.Fill};var activity=new TabPage("Activity");var devices=new TabPage("Devices");var settings=new TabPage("Preferences");var sendPage=new TabPage("Send to phone");tabs.TabPages.AddRange([activity,sendPage,devices,settings]);Controls.Add(tabs);tabs.BringToFront();
        var sendControls=new FlowLayoutPanel{Dock=DockStyle.Top,Height=220,Padding=new(20),FlowDirection=FlowDirection.TopDown,WrapContents=false};sendPage.Controls.Add(sent);sendPage.Controls.Add(sendControls);
        sendControls.Controls.Add(new Label{Text="Send files or a link to your phone",AutoSize=true,Font=new("Segoe UI",14,FontStyle.Bold)});sendControls.Controls.Add(sendTarget);
        var sendButtons=new FlowLayoutPanel{Width=740,Height=50,WrapContents=false};sendControls.Controls.Add(sendButtons);
        var sendFiles=new Button{Text="Choose files",Width=150,Height=40};sendButtons.Controls.Add(sendFiles);sendFiles.Click+=async(s,e)=>{using var picker=new OpenFileDialog{Multiselect=true};if(picker.ShowDialog(this)!=DialogResult.OK)return;await QueueFiles(picker.FileNames,sendFiles);};
        var sendText=new Button{Text="Send text / link",Width=190,Height=40};sendButtons.Controls.Add(sendText);sendText.Click+=async(s,e)=>{if(outbox==null||sendTarget.SelectedItem is not SendPhone phone){MessageBox.Show(this,"Pair a phone first.");return;}using var dialog=new Form{Text="Send text or link",Size=new(520,320),StartPosition=FormStartPosition.CenterParent};var entry=new TextBox{Multiline=true,Dock=DockStyle.Fill,MaxLength=8192};var go=new Button{Text="Send",Dock=DockStyle.Bottom,Height=45,DialogResult=DialogResult.OK};dialog.Controls.Add(entry);dialog.Controls.Add(go);if(dialog.ShowDialog(this)!=DialogResult.OK)return;try{var value=entry.Text.Trim();var kind=Uri.TryCreate(value,UriKind.Absolute,out var u)&&u.Scheme is "http" or "https"?"link":"text";await outbox.Stage(phone.Id,null,value,kind);RefreshSent();}catch(Exception ex){MessageBox.Show(this,ex.Message,"Could not send");}};
        var clipboardSend=new Button{Text="Send clipboard",Width=170,Height=40};sendButtons.Controls.Add(clipboardSend);clipboardSend.Click+=async(s,e)=>{if(outbox==null||sendTarget.SelectedItem is not SendPhone target){MessageBox.Show(this,"Pair a phone first.");return;}try{var value=Clipboard.ContainsText()?Clipboard.GetText():"";var kind=Uri.TryCreate(value,UriKind.Absolute,out var u)&&u.Scheme is "http" or "https"?"link":"text";await outbox.Stage(target.Id,null,value,kind);RefreshSent();}catch(Exception ex){MessageBox.Show(this,ex.Message,"Could not send clipboard");}};
        var cancelOutgoing=new Button{Text="Cancel selected",Width=150,Height=40};sendButtons.Controls.Add(cancelOutgoing);cancelOutgoing.Click+=(s,e)=>{if(sent.SelectedItem is SentItem item){outbox?.Cancel(item.Item.Id);RefreshSent();}};
        sendControls.Controls.Add(new Label{AutoSize=true,MaximumSize=new(740,0),Text="Drop files here, or choose files above. Open ActionBridge on the phone and select this PC to receive. Queued files stay on this PC until delivered or cancelled. 'Delivered' means saved on the phone."});
        sendPage.AllowDrop=true;sent.AllowDrop=true;sendControls.AllowDrop=true;foreach(var drop in new Control[]{sendPage,sent,sendControls}){drop.DragEnter+=(s,e)=>{if(e.Data?.GetDataPresent(DataFormats.FileDrop)==true)e.Effect=DragDropEffects.Copy;};drop.DragDrop+=async(s,e)=>{if(e.Data?.GetData(DataFormats.FileDrop) is string[] paths)await QueueFiles(paths,sendFiles);};}
        history.BorderStyle=BorderStyle.None;history.GridLines=false;history.BackColor=Color.White;
        history.Columns.Add("Time",145);history.Columns.Add("File / action",260);history.Columns.Add("Connection",100);history.Columns.Add("State",110);history.Columns.Add("Details",360);history.HideSelection=false;history.DoubleClick+=(s,e)=>{if(history.SelectedItems.Count>0&&history.SelectedItems[0].Tag is Job j&&transfers!=null&&File.Exists(transfers.Destination(j)))Process.Start(new ProcessStartInfo(transfers.Destination(j)){UseShellExecute=true});};activity.Controls.Add(history);activity.Controls.Add(emptyActivity);
        history.SizeChanged+=(s,e)=>{if(history.Columns.Count==5)history.Columns[4].Width=Math.Max(180,history.ClientSize.Width-635);};
        var tools=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=65,Padding=new(12),WrapContents=false};activity.Controls.Add(tools);
        var folder=new Button{Text="Open received files",Width=225,Height=40};folder.Click+=(s,e)=>Process.Start(new ProcessStartInfo(received){UseShellExecute=true});tools.Controls.Add(folder);
        var cancelJob=new Button{Text="Cancel selected transfer",Width=260,Height=40,Enabled=false};cancelJob.Click+=async(s,e)=>{if(history.SelectedItems.Count>0 && history.SelectedItems[0].Tag is Job job){await transfers!.Cancel(job.Request.Id,job.ClientId);RefreshHistory();}};tools.Controls.Add(cancelJob);history.SelectedIndexChanged+=(s,e)=>cancelJob.Enabled=history.SelectedItems.Count>0&&history.SelectedItems[0].Tag is Job j&&j.State is "uploading" or "queued";
        devices.Controls.Add(phones);var revoke=new Button{Text="Remove selected phone",Dock=DockStyle.Bottom,Height=42};revoke.Click+=(s,e)=>{if(phones.SelectedItem is Phone p && MessageBox.Show(this,"Remove access for "+p.Name+"?","Remove phone",MessageBoxButtons.YesNo)==DialogResult.Yes){trust!.Revoke(p.ClientId);RefreshPhones();}};devices.Controls.Add(revoke);phones.DisplayMember="Name";
        var panel=new FlowLayoutPanel{Dock=DockStyle.Fill,Padding=new(24),FlowDirection=FlowDirection.TopDown,WrapContents=false};settings.Controls.Add(panel);
        panel.Controls.Add(new Label{Text="YOUR WORKSPACE",AutoSize=true,ForeColor=Color.FromArgb(18,126,118),Font=new("Segoe UI",14,FontStyle.Bold)});
        var startup=new CheckBox{Text="Start quietly when I sign in to Windows",AutoSize=true};
        using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) startup.Checked=key?.GetValue("ActionBridge")!=null;
        startup.CheckedChanged+=(s,e)=>{using var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");if(startup.Checked) key.SetValue("ActionBridge","\""+Environment.ProcessPath+"\" --tray");else key.DeleteValue("ActionBridge",false);};panel.Controls.Add(startup);
        panel.Controls.Add(new Label{AutoSize=true,MaximumSize=new(720,0),Text="Files are saved in Downloads\\ActionBridge. Only approved phones can transfer or request actions. PDF and image printing uses your installed Windows printers.\n\nIf a job says 'uncertain', check the printer before sending again.\n\nThe app discovers PCs over local Wi-Fi or Ethernet. Guest Wi-Fi may block devices from communicating."});
        var connectionPanel=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=60,Padding=new(12),WrapContents=false};devices.Controls.Add(connectionPanel);
        var pair=new Button{Text="Connect phone · QR code",Width=230,Height=38};connectionPanel.Controls.Add(pair);pair.Click+=async(s,e)=>{if(remote==null)return;try{await EnsureRemote(true);if(remote.Config!=null)remote.ShowPairing(this);}catch(Exception ex){MessageBox.Show(this,ex.Message,"Connect phone");}};
        var disable=new Button{Text="Remove remote phone access",Width=250,Height=38};connectionPanel.Controls.Add(disable);disable.Click+=async(s,e)=>{if(remote==null||MessageBox.Show(this,"Revoke the current QR code and remove remote phone access? You can connect a phone again afterward.","Remove access",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;try{await remote.Disconnect();RefreshSendTargets();}catch(Exception ex){MessageBox.Show(this,ex.Message);}};
        panel.Controls.Add(new Label{AutoSize=true,MaximumSize=new(700,0),Text="Internet access sets up automatically. Connect your phone by scanning this PC’s QR code. The same Save, Open, Print, Copy and Link actions work nearby and remotely. Keep this PC awake. Closing this window keeps ActionBridge in the system tray."});
        remoteRetry.Tick+=async(s,e)=>{if(remote?.Enabled==true){if(remote.Config!=null)remote.Start();else await EnsureRemote();}};remoteRetry.Start();
        activityTimer.Tick+=(s,e)=>{if(historyDirty){historyDirty=false;RefreshHistory();RefreshSent();}};activityTimer.Start();
        Polish(this);
        var menu=new ContextMenuStrip();menu.Items.Add("Open ActionBridge",null,(s,e)=>ShowWindow());menu.Items.Add("Received files",null,(s,e)=>Process.Start(new ProcessStartInfo(received){UseShellExecute=true}));menu.Items.Add("Quit",null,async(s,e)=>{exiting=true;remote?.Stop();if(server!=null)await server.DisposeAsync();tray.Visible=false;Close();});tray.ContextMenuStrip=menu;tray.DoubleClick+=(s,e)=>ShowWindow();
        FormClosing+=(s,e)=>{if(!exiting){e.Cancel=true;Hide();}};
        Shown+=async(s,e)=>await Start();
    }
    sealed record SendPhone(string Id,string Label){public override string ToString()=>Label;}
    sealed record SentItem(Outgoing Item){public override string ToString()=>$"{Item.Updated.ToLocalTime():g} · {Item.Name} · {Item.State} · {Item.Offset:N0} / {Item.Size:N0} bytes";}
    void RefreshSendTargets(){var selected=(sendTarget.SelectedItem as SendPhone)?.Id;sendTarget.Items.Clear();if(remote?.Enabled==true&&remote.Config!=null)sendTarget.Items.Add(new SendPhone("remote:"+remote.Config.Room,"Phone paired by QR · Internet or nearby"));if(trust!=null)foreach(var p in trust.List())sendTarget.Items.Add(new SendPhone(p.ClientId,p.Name+" · Local pairing"));var index=Enumerable.Range(0,sendTarget.Items.Count).FirstOrDefault(i=>(sendTarget.Items[i] as SendPhone)?.Id==selected,-1);if(sendTarget.Items.Count>0)sendTarget.SelectedIndex=index>=0?index:0;}
    void RefreshSent(){if(outbox==null)return;var selected=(sent.SelectedItem as SentItem)?.Item.Id;sent.BeginUpdate();sent.Items.Clear();foreach(var item in outbox.History())sent.Items.Add(new SentItem(item));for(var i=0;i<sent.Items.Count;i++)if(((SentItem)sent.Items[i]).Item.Id==selected)sent.SelectedIndex=i;sent.EndUpdate();}
    async Task QueueFiles(string[] files,Button button){if(outbox==null||sendTarget.SelectedItem is not SendPhone phone){MessageBox.Show(this,"Pair a phone first.");return;}if(!button.Enabled)return;button.Enabled=false;try{if(files.Length>20)throw new ArgumentException("Send up to 20 files at once.");foreach(var file in files){if(!File.Exists(file))throw new ArgumentException("Choose files rather than folders.");await outbox.Stage(phone.Id,file);}RefreshSent();}catch(Exception ex){MessageBox.Show(this,ex.Message,"Could not send");}finally{button.Enabled=true;}}
    static void Polish(Control parent){foreach(Control child in parent.Controls){if(child is Button button){button.FlatStyle=FlatStyle.Flat;button.FlatAppearance.BorderSize=0;button.Cursor=Cursors.Hand;if(button.ForeColor!=Color.White){button.BackColor=Color.FromArgb(228,244,241);button.ForeColor=Color.FromArgb(18,126,118);}}if(child is TabPage)child.BackColor=Color.White;Polish(child);}}
    async Task EnsureRemote(bool enable=false){if(remote==null||enrolling||(!enable&&!remote.Enabled)||remote.Config!=null&&remote.Enabled)return;enrolling=true;remoteStatus.Text="Setting up internet access…";try{if(enable&&!remote.Enabled&&remote.Config!=null)await remote.Disconnect();await remote.Configure();remoteStatus.Text="Internet access ready · Connect phone to pair";RefreshSendTargets();}catch(Exception ex){remoteStatus.Text="Internet access unavailable · "+ex.Message;}finally{enrolling=false;}}
    void ShowWindow(){Show();WindowState=FormWindowState.Normal;Activate();}
    void RefreshPhones(){phones.Items.Clear();if(trust!=null)phones.Items.AddRange(trust.List());RefreshSendTargets();}
    void RefreshHistory(){var selected=history.SelectedItems.Count>0?(history.SelectedItems[0].Tag as Job)?.Request.Id:null;if(transfers==null)return;history.BeginUpdate();history.Items.Clear();foreach(var j in transfers.History())history.Items.Add(new ListViewItem([j.Updated.ToLocalTime().ToString("g"),j.Request.Name+" · "+j.Request.Action,j.ClientId.StartsWith("remote:")?"Internet":"Local",j.State,j.Message??$"{j.Offset:N0} / {j.Request.Size:N0} bytes"]){Tag=j,ForeColor=j.State is "failed" or "uncertain"?Color.FromArgb(167,72,44):Color.FromArgb(29,43,57)});foreach(ListViewItem item in history.Items)if(item.Tag is Job j&&j.Request.Id==selected)item.Selected=true;emptyActivity.Visible=history.Items.Count==0;history.EndUpdate();}
    async Task<bool> Approve(PairRequest r,string ip) {
        var task=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        BeginInvoke(()=>{
            ShowWindow();
            var dialog=new Form{Text="Allow this phone?",Size=new(440,240),StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false};
            var label=new Label{Dock=DockStyle.Fill,Padding=new(20),Text=$"{r.Name} ({ip}) wants to connect.\n\nAllow only if you just tapped this PC on your phone. The device name is supplied by the sender.",AutoSize=false};dialog.Controls.Add(label);
            var buttons=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=55,FlowDirection=FlowDirection.RightToLeft};var allow=new Button{Text="Allow",Width=110,Height=35};var deny=new Button{Text="Decline",Width=110,Height=35};buttons.Controls.Add(allow);buttons.Controls.Add(deny);dialog.Controls.Add(buttons);
            allow.Click+=(s,e)=>{task.TrySetResult(true);dialog.Close();};deny.Click+=(s,e)=>dialog.Close();dialog.FormClosed+=(s,e)=>{task.TrySetResult(false);RefreshPhones();};
            var timer=new System.Windows.Forms.Timer{Interval=55000};timer.Tick+=(s,e)=>{timer.Stop();dialog.Close();};dialog.FormClosed+=(s,e)=>timer.Dispose();timer.Start();dialog.Show(this);
        });return await task.Task;
    }
    async Task Start() {
        try {
            Directory.CreateDirectory(root);Directory.CreateDirectory(received);
            remote=new(root,received);remote.Changed+=message=>{if(!IsDisposed&&IsHandleCreated)BeginInvoke(()=>remoteStatus.Text=message);};
            var certPath=Path.Combine(root,"identity.dat");
            if(!File.Exists(certPath)) File.WriteAllBytes(certPath,ProtectedData.Protect(ServerIdentity.CreatePfx(),null,DataProtectionScope.CurrentUser));
            var cert=ServerIdentity.Load(ProtectedData.Unprotect(File.ReadAllBytes(certPath),null,DataProtectionScope.CurrentUser));
            var idPath=Path.Combine(root,"pc-id.txt");var id=File.Exists(idPath)?File.ReadAllText(idPath):Guid.NewGuid().ToString();File.WriteAllText(idPath,id);
            trust=new(root);trust.Changed+=()=>{if(!IsDisposed&&IsHandleCreated)BeginInvoke(()=>RefreshPhones());};var sink=new WindowsActions(this);transfers=new(Path.Combine(root,"jobs"),received,sink);
            transfers.Changed+=j=>{if(!IsDisposed&&IsHandleCreated)BeginInvoke(()=>historyDirty=true);};
            outbox=new(Path.Combine(root,"outbox"));outbox.Changed+=()=>{if(!IsDisposed&&IsHandleCreated)BeginInvoke(()=>historyDirty=true);};
            server=new(id,Environment.MachineName,cert,trust,transfers,sink,Approve,()=>remote?.Enabled==true&&remote.Config!=null?remote.PairingCode():null,outbox);await server.StartAsync();
            using(var handler=new HttpClientHandler{ServerCertificateCustomValidationCallback=(_,remote,_,_)=>remote!=null&&SHA256.HashData(remote.RawData).SequenceEqual(SHA256.HashData(cert.RawData))})
            using(var probe=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(8)}) {
                (await probe.GetAsync($"https://127.0.0.1:{Wire.ApiPort}/v1/hello")).EnsureSuccessStatusCode();
            }
            status.Text=Environment.MachineName+" · Ready for local transfers";RefreshHistory();RefreshPhones();
            remote.Attach(transfers,sink,id,server.Fingerprint,outbox);if(remote.Enabled){remote.Start();if(remote.Config==null)_=EnsureRemote();}else remoteStatus.Text="Internet access is off · Connect phone to enable";
            if(Environment.GetCommandLineArgs().Contains("--tray"))Hide();
        } catch(Exception ex){
            if(server!=null){await server.DisposeAsync();server=null;}
            Directory.CreateDirectory(root);File.WriteAllText(Path.Combine(root,"startup-error.txt"),DateTimeOffset.Now+"\n"+ex);
            status.Text="Receiver unavailable: "+ex.GetBaseException().Message;
            MessageBox.Show(this,status.Text+"\n\nDetails: "+Path.Combine(root,"startup-error.txt"),"ActionBridge could not start");
        }
    }
    protected override void Dispose(bool disposing){if(disposing){activityTimer.Dispose();remoteRetry.Dispose();remote?.Dispose();tray.Dispose();}base.Dispose(disposing);}
}
