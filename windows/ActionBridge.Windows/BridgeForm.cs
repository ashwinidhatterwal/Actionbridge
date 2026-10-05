using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ActionBridge.Core;
using Microsoft.Win32;
namespace ActionBridge.Windows;
public sealed partial class BridgeForm : Form {
    readonly string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ActionBridge");
    readonly string received=Path.Combine(KnownFolders.Downloads(),"ActionBridge");
    readonly Label status=new(){AutoSize=true,Text="Starting secure receiver…"};
    readonly ListView history=new(){Dock=DockStyle.Fill,View=View.Details,FullRowSelect=true};
    readonly Label emptyActivity=new(){Dock=DockStyle.Top,Height=70,Padding=new(18),Text="Ready for your next file\nConnect a device, choose an action, and send. Your activity appears here.",ForeColor=Color.FromArgb(91,105,115)};
    readonly NotifyIcon tray=new(){Visible=true,Text="ActionBridge",Icon=SystemIcons.Application};
    RemoteConnection? remote;
    readonly Label remoteStatus=new(){AutoSize=true,MaximumSize=new(650,0),Text="Internet connection · starting…",ForeColor=Color.FromArgb(91,105,115)};
    readonly System.Windows.Forms.Timer remoteRetry=new(){Interval=30000};readonly System.Windows.Forms.Timer activityTimer=new(){Interval=300};bool enrolling,historyDirty;
    BridgeServer? server;TrustStore? trust;Transfers? transfers;Outbox? outbox;Computers? computers;readonly ListBox sendTarget=new(){Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,DrawMode=DrawMode.OwnerDrawFixed,ItemHeight=66,IntegralHeight=false};readonly ListBox sent=new(){Dock=DockStyle.Fill};bool exiting;
    sealed record SendPhone(string Id,string Label){public override string ToString()=>Label;}
    sealed record SentItem(Outgoing Item){public override string ToString()=>$"{Item.Name}   ·   {FriendlyState(Item.State)}   ·   {Bytes(Item.Offset)} / {Bytes(Item.Size)}";}
    void RefreshSendTargets(){
        var selected=(sendTarget.SelectedItem as SendPhone)?.Id??selectedDevice;
        var values=new List<SendPhone>();
        if(computers!=null)foreach(var p in computers.List())values.Add(new(p.Recipient,p.Name+" · "+(p.Internet?"Internet":"Nearby")));
        if(remote?.Enabled==true&&remote.Config!=null&&remote.HasLinkedDevice)values.Add(new("remote:"+remote.Config.Room,"Linked internet device · QR connection"));
        if(trust!=null)foreach(var p in trust.List())values.Add(new(p.ClientId,p.Name+" · Nearby phone"));
        if(!sendTarget.Items.Cast<SendPhone>().SequenceEqual(values)){
            refreshingDevices=true;sendTarget.BeginUpdate();sendTarget.Items.Clear();sendTarget.Items.AddRange(values.Cast<object>().ToArray());
            sendTarget.SelectedIndex=values.FindIndex(p=>p.Id==selected);sendTarget.EndUpdate();refreshingDevices=false;
        }
        UpdateSendControls();
    }
    void RefreshSent(){if(outbox==null)return;var selected=(sent.SelectedItem as SentItem)?.Item.Id;sent.BeginUpdate();sent.Items.Clear();foreach(var item in outbox.History())sent.Items.Add(new SentItem(item));for(var i=0;i<sent.Items.Count;i++)if(((SentItem)sent.Items[i]).Item.Id==selected)sent.SelectedIndex=i;sent.EndUpdate();recent.Text=outbox.History().FirstOrDefault() is {} latest?latest.Name+" · "+FriendlyState(latest.State):"No transfers yet. Sent and received items appear in Activity.";}
    async Task QueueFiles(string[] files,Button button){if(outbox==null||sendTarget.SelectedItem is not SendPhone phone){MessageBox.Show(this,"Add a computer or pair a phone first.");return;}if(!button.Enabled)return;stagingFiles=true;UpdateSendControls();try{if(files.Length>20)throw new ArgumentException("Send up to 20 files at once.");foreach(var file in files){if(!File.Exists(file))throw new ArgumentException("Choose files rather than folders.");await outbox.Stage(phone.Id,file);}RefreshSent();}catch(Exception ex){MessageBox.Show(this,ex.Message,"Could not send");}finally{stagingFiles=false;UpdateSendControls();}}
    async Task AddComputer(){if(computers==null)return;using var d=new Form{Text="Add computer",Size=new(570,245),StartPosition=FormStartPosition.CenterParent};var hint=new Label{Dock=DockStyle.Top,Height=70,Padding=new(12),Text="Nearby: enter the other computer’s local IP.\nInternet: paste its private ActionBridge pairing code.\nThe destination stays saved until you remove it."};var entry=new TextBox{Dock=DockStyle.Top};var connect=new Button{Text="Connect",Dock=DockStyle.Bottom,Height=40};d.Controls.Add(entry);d.Controls.Add(hint);d.Controls.Add(connect);connect.Click+=async(_,_)=>{connect.Enabled=false;try{var value=entry.Text.Trim();if(value.StartsWith("abremote:"))computers.AddRemote(value);else await computers.AddLocal(value);RefreshSendTargets();d.Close();}catch(Exception e){MessageBox.Show(d,e.Message,"Could not connect");connect.Enabled=true;}};d.ShowDialog(this);await Task.CompletedTask;}
    async Task DiscoverComputers(){if(computers==null)return;try{var endpoints=await computers.Discover();if(endpoints.Length==0){MessageBox.Show(this,"No nearby computers found. Use Add computer and enter its local IP.");return;}using var d=new Form{Text="Nearby computers",Size=new(540,340),StartPosition=FormStartPosition.CenterParent};var list=new ListBox{Dock=DockStyle.Fill};list.Items.AddRange(endpoints.Select(p=>p.Name+" · "+p.Host).ToArray());list.SelectedIndex=0;var connect=new Button{Text="Connect and request approval",Dock=DockStyle.Bottom,Height=40};d.Controls.Add(list);d.Controls.Add(connect);connect.Click+=async(_,_)=>{connect.Enabled=false;try{var p=endpoints[list.SelectedIndex];await computers.AddLocal(p.Host,p.Port);RefreshSendTargets();d.Close();}catch(Exception e){MessageBox.Show(d,e.Message);connect.Enabled=true;}};d.ShowDialog(this);}catch(Exception e){MessageBox.Show(this,e.Message);}}
    static void Polish(Control parent){foreach(Control child in parent.Controls){if(child is Button button){button.FlatStyle=FlatStyle.Flat;button.FlatAppearance.BorderSize=0;button.Cursor=Cursors.Hand;if(button.ForeColor!=Color.White){button.BackColor=Color.FromArgb(228,244,241);button.ForeColor=Color.FromArgb(18,126,118);}}if(child is TabPage)child.BackColor=Color.White;Polish(child);}}
    async Task EnsureRemote(bool enable=false){if(remote==null||enrolling||(!enable&&!remote.Enabled)||remote.Config!=null&&remote.Enabled)return;enrolling=true;remoteStatus.Text="Setting up internet access…";try{if(enable&&!remote.Enabled&&remote.Config!=null)await remote.Disconnect();await remote.Configure();remoteStatus.Text="Internet access ready · Connect device to pair";RefreshSendTargets();}catch(Exception ex){remoteStatus.Text="Internet access unavailable · "+ex.Message;}finally{enrolling=false;}}
    void ShowWindow(){Show();WindowState=FormWindowState.Normal;Activate();}
    void RefreshPhones()=>RefreshSendTargets();
    void RefreshHistory(){var selected=history.SelectedItems.Count>0?(history.SelectedItems[0].Tag as Job)?.Request.Id:null;if(transfers==null)return;history.BeginUpdate();history.Items.Clear();foreach(var j in transfers.History())history.Items.Add(new ListViewItem([j.Updated.ToLocalTime().ToString("g"),j.Request.Name+" · "+j.Request.Action,j.ClientId.StartsWith("remote:")?"Internet":"Local",FriendlyState(j.State),j.Message??$"{Bytes(j.Offset)} / {Bytes(j.Request.Size)}"]){Tag=j,ForeColor=j.State is "failed" or "uncertain"?Color.FromArgb(167,72,44):Color.FromArgb(29,43,57)});foreach(ListViewItem item in history.Items)if(item.Tag is Job j&&j.Request.Id==selected)item.Selected=true;emptyActivity.Visible=history.Items.Count==0;history.EndUpdate();}
    async Task<bool> Approve(PairRequest r,string ip) {
        var task=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        BeginInvoke(()=>{
            ShowWindow();
            var dialog=new Form{Text="Allow this device?",Size=new(440,240),StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false};
            var label=new Label{Dock=DockStyle.Fill,Padding=new(20),Text=$"{r.Name} ({ip}) wants to connect.\n\nAllow only if you requested this connection. The device name is supplied by the sender.",AutoSize=false};dialog.Controls.Add(label);
            var buttons=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=55,FlowDirection=FlowDirection.RightToLeft};var allow=new Button{Text="Allow",Width=110,Height=35};var deny=new Button{Text="Decline",Width=110,Height=35};buttons.Controls.Add(allow);buttons.Controls.Add(deny);dialog.Controls.Add(buttons);
            allow.Click+=(s,e)=>{task.TrySetResult(true);dialog.Close();};deny.Click+=(s,e)=>dialog.Close();dialog.FormClosed+=(s,e)=>{task.TrySetResult(false);RefreshPhones();};
            var timer=new System.Windows.Forms.Timer{Interval=55000};timer.Tick+=(s,e)=>{timer.Stop();dialog.Close();};dialog.FormClosed+=(s,e)=>timer.Dispose();timer.Start();dialog.Show(this);
        });return await task.Task;
    }
    async Task Start() {
        try {
            Directory.CreateDirectory(root);Directory.CreateDirectory(received);
            remote=new(root,received);remote.Changed+=message=>{if(!IsDisposed&&IsHandleCreated)BeginInvoke(()=>{remoteStatus.Text=message;RefreshSendTargets();});};
            var certPath=Path.Combine(root,"identity.dat");
            if(!File.Exists(certPath)) File.WriteAllBytes(certPath,ProtectedData.Protect(ServerIdentity.CreatePfx(),null,DataProtectionScope.CurrentUser));
            var cert=ServerIdentity.Load(ProtectedData.Unprotect(File.ReadAllBytes(certPath),null,DataProtectionScope.CurrentUser));
            var idPath=Path.Combine(root,"pc-id.txt");var id=File.Exists(idPath)?File.ReadAllText(idPath):Guid.NewGuid().ToString();File.WriteAllText(idPath,id);
            trust=new(root);trust.Changed+=()=>{if(!IsDisposed&&IsHandleCreated)BeginInvoke(()=>RefreshPhones());};var sink=new WindowsActions(this);transfers=new(Path.Combine(root,"jobs"),received,sink);
            transfers.Changed+=j=>{if(!IsDisposed&&IsHandleCreated)BeginInvoke(()=>historyDirty=true);};
            outbox=new(Path.Combine(root,"outbox"));computers=new(root,id,Environment.MachineName,outbox,transfers,b=>ProtectedData.Protect(b,null,DataProtectionScope.CurrentUser),b=>ProtectedData.Unprotect(b,null,DataProtectionScope.CurrentUser));computers.Changed+=()=>{if(!IsDisposed&&IsHandleCreated)BeginInvoke(()=>RefreshSendTargets());};computers.Start();outbox.Changed+=()=>{if(!IsDisposed&&IsHandleCreated)BeginInvoke(()=>historyDirty=true);};
            server=new(id,Environment.MachineName,cert,trust,transfers,sink,Approve,()=>remote?.Enabled==true&&remote.Config!=null?remote.PairingCode():null,outbox);await server.StartAsync();
            using(var handler=new HttpClientHandler{ServerCertificateCustomValidationCallback=(_,remote,_,_)=>remote!=null&&SHA256.HashData(remote.RawData).SequenceEqual(SHA256.HashData(cert.RawData))})
            using(var probe=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(8)}) {
                (await probe.GetAsync($"https://127.0.0.1:{Wire.ApiPort}/v1/hello")).EnsureSuccessStatusCode();
            }
            status.Text="This computer · Ready to receive";RefreshHistory();RefreshPhones();
            remote.Attach(transfers,sink,id,server.Fingerprint,outbox);if(remote.Enabled){remote.Start();if(remote.Config==null)_=EnsureRemote();}else remoteStatus.Text="Internet access is off · Connect device to enable";
            if(Environment.GetCommandLineArgs().Contains("--tray"))Hide();
        } catch(Exception ex){
            if(server!=null){await server.DisposeAsync();server=null;}
            Directory.CreateDirectory(root);File.WriteAllText(Path.Combine(root,"startup-error.txt"),DateTimeOffset.Now+"\n"+ex);
            status.Text="Receiver unavailable: "+ex.GetBaseException().Message;
            MessageBox.Show(this,status.Text+"\n\nDetails: "+Path.Combine(root,"startup-error.txt"),"ActionBridge could not start");
        }
    }
    protected override void Dispose(bool disposing){if(disposing){activityTimer.Dispose();remoteRetry.Dispose();computers?.Dispose();remote?.Dispose();tray.Dispose();}base.Dispose(disposing);}
}
