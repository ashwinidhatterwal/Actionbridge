using System.Diagnostics;
using Microsoft.Win32;
using ActionBridge.Core;

namespace ActionBridge.Windows;

public sealed partial class BridgeForm {
    static readonly Color Accent=Color.FromArgb(18,126,118),Ink=Color.FromArgb(29,43,57),Muted=Color.FromArgb(91,105,115);
    readonly Label sendHeading=new(){AutoSize=true,Font=new("Segoe UI",21,FontStyle.Bold)};
    readonly Label sendHint=new(){AutoSize=true,MaximumSize=new(540,0),ForeColor=Muted};
    readonly Label recent=new(){AutoSize=true,MaximumSize=new(540,0),ForeColor=Muted};
    Button fileButton=null!,textButton=null!,clipboardButton=null!;
    string? selectedDevice;bool refreshingDevices,stagingFiles;
    public BridgeForm(bool startReceiver=true){
        Text="ActionBridge";Size=new(1120,780);MinimumSize=new(850,620);Font=new("Segoe UI",10);AutoScaleMode=AutoScaleMode.Dpi;AutoScaleDimensions=new(96,96);
        BackColor=Color.FromArgb(244,247,250);ForeColor=Ink;Icon=Icon.ExtractAssociatedIcon(Environment.ProcessPath!)??SystemIcons.Application;tray.Icon=Icon;
        try{selectedDevice=File.ReadAllText(Path.Combine(root,"selected-device.txt"));}catch{}
        var shell=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Padding=new(24)};
        shell.RowStyles.Add(new(SizeType.AutoSize));shell.RowStyles.Add(new(SizeType.Percent,100));Controls.Add(shell);
        var header=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=2,Margin=new(0,0,0,20)};header.ColumnStyles.Add(new(SizeType.Percent,100));header.ColumnStyles.Add(new(SizeType.AutoSize));
        var brand=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=1,Margin=Padding.Empty};brand.Controls.Add(Title("ActionBridge",22));status.ForeColor=Muted;status.Margin=new(0,0,0,8);brand.Controls.Add(status);header.Controls.Add(brand,0,0);
        header.SizeChanged+=(_,_)=>status.MaximumSize=new(Math.Max(120,header.ClientSize.Width-Px(200)),0);
        header.Controls.Add(ActionButton("+ Add device",AddDevice,true),1,0);shell.Controls.Add(header,0,0);
        var body=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2};body.ColumnStyles.Add(new(SizeType.Absolute,156));body.ColumnStyles.Add(new(SizeType.Percent,100));shell.Controls.Add(body,0,1);
        var nav=Column();nav.Padding=new(0,8,16,0);body.Controls.Add(nav,0,0);
        var tabs=new PageHost{Dock=DockStyle.Fill};body.Controls.Add(tabs,1,0);
        var home=new Panel{Text="Home",BackColor=BackColor,Padding=new(0)};var activity=new Panel{Text="Activity",BackColor=Color.White,Padding=new(16)};var settings=new Panel{Text="Settings",BackColor=Color.White,Padding=new(16)};
        tabs.SetPages([home,activity,settings]);
        nav.Controls.Add(Description("WORKSPACE",true));var navigation=new List<Button>();
        foreach(var page in new[]{home,activity,settings}){var button=ActionButton(page.Text,()=>tabs.SelectedPage=page);button.MinimumSize=new(128,48);button.Width=128;button.TextAlign=ContentAlignment.MiddleLeft;navigation.Add(button);nav.Controls.Add(button);}
        void PaintNavigation(){for(int i=0;i<navigation.Count;i++){navigation[i].BackColor=tabs.SelectedIndex==i?Color.FromArgb(218,238,234):BackColor;navigation[i].ForeColor=tabs.SelectedIndex==i?Accent:Muted;navigation[i].Font=new Font(Font,tabs.SelectedIndex==i?FontStyle.Bold:FontStyle.Regular);}}
        tabs.SelectedIndexChanged+=(_,_)=>PaintNavigation();PaintNavigation();
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1};layout.ColumnStyles.Add(new(SizeType.Percent,36));layout.ColumnStyles.Add(new(SizeType.Percent,64));home.Controls.Add(layout);
        var devicePanel=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=5,ColumnCount=1,Padding=new(16),BackColor=Color.White,Margin=new(0,0,16,0)};
        devicePanel.RowStyles.Add(new(SizeType.AutoSize));devicePanel.RowStyles.Add(new(SizeType.AutoSize));devicePanel.RowStyles.Add(new(SizeType.AutoSize));devicePanel.RowStyles.Add(new(SizeType.Percent,100));devicePanel.RowStyles.Add(new(SizeType.AutoSize));
        devicePanel.Controls.Add(Title("Your devices",15),0,0);devicePanel.Controls.Add(Description("Select where you want to send."),0,1);devicePanel.Controls.Add(ActionButton("Find nearby",AddDevice),0,2);devicePanel.Controls.Add(sendTarget,0,3);
        devicePanel.Controls.Add(ActionButton("Manage selected device",ManageSelectedDevice),0,4);layout.Controls.Add(devicePanel,0,0);
        sendTarget.AccessibleName="Saved devices and connection status";
        sendTarget.DrawItem+=(_,e)=>{if(e.Index<0)return;var p=(SendPhone)sendTarget.Items[e.Index];bool selected=(e.State&DrawItemState.Selected)!=0;using var brush=new SolidBrush(selected?Color.FromArgb(228,244,241):Color.White);e.Graphics.FillRectangle(brush,e.Bounds);
            var online=p.Connected;using var dot=new SolidBrush(online?Accent:Color.FromArgb(147,158,169));e.Graphics.FillEllipse(dot,e.Bounds.X+Px(12),e.Bounds.Y+Px(16),Px(8),Px(8));
            using var nameFont=new Font(Font,FontStyle.Bold);TextRenderer.DrawText(e.Graphics,p.Name,nameFont,new Rectangle(e.Bounds.X+Px(30),e.Bounds.Y+Px(9),e.Bounds.Width-Px(42),Px(25)),Ink,TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine);
            TextRenderer.DrawText(e.Graphics,(online?"Connected":"Disconnected")+" · "+p.Route,Font,new Rectangle(e.Bounds.X+Px(12),e.Bounds.Y+Px(38),e.Bounds.Width-Px(24),Px(24)),online?Accent:Muted,TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine);e.DrawFocusRectangle();};
        sendTarget.SelectedIndexChanged+=(_,_)=>{if(!refreshingDevices){selectedDevice=(sendTarget.SelectedItem as SendPhone)?.Id;try{Directory.CreateDirectory(root);File.WriteAllText(Path.Combine(root,"selected-device.txt"),selectedDevice??"");}catch{}UpdateSendControls();}};
        var workspace=Column();workspace.Dock=DockStyle.Fill;workspace.AutoScroll=true;workspace.Padding=new(16);workspace.BackColor=Color.White;layout.Controls.Add(workspace,1,0);
        workspace.Controls.Add(sendHeading);workspace.Controls.Add(sendHint);workspace.Controls.Add(Description("SEND FILES",true));
        var drop=Description("Drop files here\nOr choose files with the button below.");drop.Padding=new(18);drop.BackColor=Color.FromArgb(244,247,250);drop.MinimumSize=new(400,100);workspace.Controls.Add(drop);
        fileButton=ActionButton("Send files…",async()=>{using var picker=new OpenFileDialog{Multiselect=true,Title="Choose files to send"};if(picker.ShowDialog(this)==DialogResult.OK)await QueueFiles(picker.FileNames,fileButton);},true);fileButton.MinimumSize=new(230,48);fileButton.Width=230;workspace.Controls.Add(fileButton);
        workspace.Controls.Add(Description("QUICK ACTIONS",true));var textRow=new FlowLayoutPanel{AutoSize=true,WrapContents=true};textButton=ActionButton("Write text or link",SendText);clipboardButton=ActionButton("Send clipboard",SendClipboard);textRow.Controls.AddRange([textButton,clipboardButton]);workspace.Controls.Add(textRow);
        workspace.Controls.Add(Description("Files are saved on the other device. You can track every transfer in Activity."));workspace.Controls.Add(Description("LATEST SENT ITEM",true));workspace.Controls.Add(recent);
        workspace.Controls.Add(ActionButton("View all activity",()=>tabs.SelectedPage=activity));
        workspace.SizeChanged+=(_,_)=>{var width=Math.Max(120,workspace.ClientSize.Width-workspace.Padding.Horizontal-Px(24));foreach(Control child in workspace.Controls){child.MaximumSize=new(width,0);if(child is FlowLayoutPanel row){row.MaximumSize=new(width,0);row.Width=width;}}drop.MinimumSize=new(Math.Min(Px(400),width),Px(100));};
        DpiChanged+=(_,_)=>sendTarget.ItemHeight=Px(76);
        foreach(Control target in new Control[]{home,workspace,drop}){target.AllowDrop=true;target.DragEnter+=(_,e)=>{if(!stagingFiles&&sendTarget.SelectedItem!=null&&e.Data?.GetDataPresent(DataFormats.FileDrop)==true)e.Effect=DragDropEffects.Copy;};target.DragDrop+=async(_,e)=>{if(e.Data?.GetData(DataFormats.FileDrop) is string[] paths)await QueueFiles(paths,fileButton);};}
        var records=new TabControl{Dock=DockStyle.Fill};activity.Controls.Add(records);var receivedPage=new TabPage("Received");var sentPage=new TabPage("Sent");records.TabPages.AddRange([receivedPage,sentPage]);
        history.BorderStyle=BorderStyle.None;history.FullRowSelect=true;history.HideSelection=false;history.Columns.Add("When",145);history.Columns.Add("Item / action",260);history.Columns.Add("Route",90);history.Columns.Add("Status",145);history.Columns.Add("Details",300);history.SizeChanged+=(_,_)=>history.Columns[4].Width=Math.Max(160,history.ClientSize.Width-650);
        history.DoubleClick+=(_,_)=>OpenSelectedReceived();receivedPage.Controls.Add(history);emptyActivity.Text="Nothing received yet.\nAdd a device to start sharing with this computer.";receivedPage.Controls.Add(emptyActivity);
        var receiveTools=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=58};receiveTools.Controls.Add(ActionButton("Open selected file",OpenSelectedReceived));receiveTools.Controls.Add(ActionButton("Received folder",OpenFolder));var cancel=ActionButton("Cancel transfer",async()=>{if(history.SelectedItems.Count>0&&history.SelectedItems[0].Tag is Job j){await transfers!.Cancel(j.Request.Id,j.ClientId);RefreshHistory();}});cancel.Enabled=false;receiveTools.Controls.Add(cancel);history.SelectedIndexChanged+=(_,_)=>cancel.Enabled=history.SelectedItems.Count>0&&history.SelectedItems[0].Tag is Job j&&j.State is "uploading" or "queued";receivedPage.Controls.Add(receiveTools);
        sent.HorizontalScrollbar=true;sent.BorderStyle=BorderStyle.None;sent.ItemHeight=34;sentPage.Controls.Add(sent);var sendTools=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=58};var cancelSent=ActionButton("Cancel selected item",()=>{if(sent.SelectedItem is SentItem item){outbox?.Cancel(item.Item.Id);RefreshSent();}});cancelSent.Enabled=false;sent.SelectedIndexChanged+=(_,_)=>cancelSent.Enabled=sent.SelectedItem is SentItem item&&item.Item.State is "queued" or "sending" or "retrying";sendTools.Controls.Add(cancelSent);sentPage.Controls.Add(sendTools);
        var prefs=Column();prefs.Dock=DockStyle.Fill;prefs.AutoScroll=true;prefs.Padding=new(16);settings.Controls.Add(prefs);prefs.Controls.Add(Title("Receiving & startup",17));
        var startup=new CheckBox{Text="Start receiving when I sign in",AutoSize=true,Margin=new(0,12,0,12)};using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))startup.Checked=key?.GetValue("ActionBridge")!=null;startup.CheckedChanged+=(_,_)=>{using var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");if(startup.Checked)key.SetValue("ActionBridge","\""+Environment.ProcessPath+"\" --tray");else key.DeleteValue("ActionBridge",false);};prefs.Controls.Add(startup);
        prefs.Controls.Add(Description("Closing this window keeps receiving in the system tray.\nChoose Quit from the tray menu to stop."));prefs.Controls.Add(ActionButton("Open received folder",OpenFolder));prefs.Controls.Add(Title("Printing",17));prefs.Controls.Add(Description("Add printers in Windows Settings. On your phone, choose Print to select\npaper, copies and other options. A submitted job is not a physical print confirmation."));prefs.Controls.Add(ActionButton("Windows printer settings",()=>Process.Start(new ProcessStartInfo("ms-settings:printers"){UseShellExecute=true})));
        prefs.Controls.Add(Title("Connection help",17));prefs.Controls.Add(Description("Nearby: use the same Wi-Fi or Ethernet and approve the connection.\nInternet: add a device using this computer’s private code. Keep both devices awake.\nAn unavailable device stays saved. Internet connections depend on both networks."));prefs.Controls.Add(ActionButton("Show this computer’s QR code",ShowCode));prefs.Controls.Add(ActionButton("Remove internet access",async()=>{if(remote==null||MessageBox.Show(this,"Revoke this computer’s QR code and remove internet access?","Remove internet access",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;try{await remote.Disconnect();RefreshSendTargets();}catch(Exception e){MessageBox.Show(this,e.Message);}}));
        prefs.Controls.Add(Title("Connection details",17));remoteStatus.AutoSize=true;prefs.Controls.Add(remoteStatus);prefs.Controls.Add(Description("ActionBridge 0.8.0 · No advertising or analytics.\nPairing codes grant access to this computer. Share them only with devices you trust."));
        remoteRetry.Tick+=async(_,_)=>{if(remote?.Enabled==true){if(remote.Config!=null)remote.Start();else await EnsureRemote();}};remoteRetry.Start();activityTimer.Tick+=(_,_)=>{if(historyDirty){historyDirty=false;RefreshHistory();RefreshSent();}};activityTimer.Start();presenceTimer.Tick+=(_,_)=>RefreshSendTargets();presenceTimer.Start();
        var menu=new ContextMenuStrip();menu.Items.Add("Open ActionBridge",null,(_,_)=>ShowWindow());menu.Items.Add("Received files",null,(_,_)=>OpenFolder());menu.Items.Add("Quit",null,async(_,_)=>{exiting=true;computers?.Dispose();remote?.Stop();if(server!=null)await server.DisposeAsync();tray.Visible=false;Close();});tray.ContextMenuStrip=menu;tray.DoubleClick+=(_,_)=>ShowWindow();FormClosing+=(_,e)=>{if(!exiting){e.Cancel=true;Hide();}};Shown+=async(_,_)=>{sendTarget.ItemHeight=Px(76);if(startReceiver)await Start();};if(!startReceiver)tray.Visible=false;UpdateSendControls();
    }
    readonly System.Windows.Forms.Timer presenceTimer=new(){Interval=2000};
    sealed class PageHost:Panel {
        Panel[] pages=[];int selected=-1;
        public event EventHandler? SelectedIndexChanged;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int SelectedIndex {get=>selected;set{if(value<0||value>=pages.Length||value==selected)return;selected=value;for(int i=0;i<pages.Length;i++)pages[i].Visible=i==selected;SelectedIndexChanged?.Invoke(this,EventArgs.Empty);}}
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public Panel? SelectedPage {get=>selected<0?null:pages[selected];set{SelectedIndex=Array.IndexOf(pages,value);}}
        public void SetPages(Panel[] value){pages=value;foreach(var page in pages){page.Dock=DockStyle.Fill;Controls.Add(page);}SelectedIndex=0;}
    }
    static FlowLayoutPanel Column()=>new(){FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoSize=false,Dock=DockStyle.Fill};
    static Label Title(string value,int size)=>new(){Text=value,Font=new("Segoe UI",size,FontStyle.Bold),AutoSize=true,Margin=new(0,0,0,12)};
    static Label Description(string value,bool heading=false)=>new(){Text=value,AutoSize=true,MaximumSize=new(620,0),ForeColor=heading?Accent:Muted,Font=new("Segoe UI",heading?9:10,heading?FontStyle.Bold:FontStyle.Regular),Margin=new(0,heading?18:4,0,12)};
    static Button ActionButton(string value,Action action,bool primary=false){var b=new Button{Text=value,AutoSize=true,MinimumSize=new(140,44),Height=44,Padding=new(12,4,12,4),FlatStyle=FlatStyle.Flat,BackColor=primary?Accent:Color.FromArgb(240,245,247),ForeColor=primary?Color.White:Accent,Cursor=Cursors.Hand,Margin=new(0,6,10,10)};b.FlatAppearance.BorderSize=0;b.Click+=(_,_)=>action();return b;}
    void UpdateSendControls(){var device=sendTarget.SelectedItem as SendPhone;sendHeading.Text=device==null?"Ready to share":("Send to "+device.Label.Split(" · ")[0]);sendHeading.MaximumSize=new(Math.Max(200,(sendHeading.Parent?.ClientSize.Width??500)-56),0);sendHint.Text=device==null?"Find a nearby phone or computer, or add one with a QR code.":device.Connected?"Connected · "+device.Route+" · Ready to receive":"Disconnected · Device stays saved. Files will wait until it reconnects.";if(fileButton!=null){fileButton.Enabled=device!=null&&outbox!=null&&!stagingFiles;textButton.Enabled=clipboardButton.Enabled=fileButton.Enabled;}recent.Text=outbox?.History().FirstOrDefault() is {} item?item.Name+" · "+FriendlyState(item.State):"No transfers yet. Your history appears in Activity.";}
    void OpenFolder(){Directory.CreateDirectory(received);Process.Start(new ProcessStartInfo(received){UseShellExecute=true});}
    void OpenSelectedReceived(){if(history.SelectedItems.Count>0&&history.SelectedItems[0].Tag is Job j&&transfers!=null&&File.Exists(transfers.Destination(j)))Process.Start(new ProcessStartInfo(transfers.Destination(j)){UseShellExecute=true});}
    void ShowCode()=>ShowConnectionDialog(1);
    void AddDevice()=>ShowConnectionDialog();
    void ManageSelectedDevice(){if(sendTarget.SelectedItem is not SendPhone target){MessageBox.Show(this,"Select a device on Home first.");return;}if(target.Id.StartsWith("remote:")){ShowCode();return;}if(MessageBox.Show(this,"Remove "+target.Label.Split(" · ")[0]+"? You can add it again later.","Remove device",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;if(computers?.Contains(target.Id)==true)computers.Remove(target.Id);else trust?.Revoke(target.Id);selectedDevice=null;RefreshSendTargets();}
    async void SendText(){if(outbox==null||sendTarget.SelectedItem is not SendPhone target)return;
        using var d=new Form{Text="Send to "+target.Name,ClientSize=new(540,350),MinimumSize=new(420,300),Font=Font,AutoScaleMode=AutoScaleMode.Dpi,StartPosition=FormStartPosition.CenterParent};
        var panel=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new(20)};panel.RowStyles.Add(new(SizeType.AutoSize));panel.RowStyles.Add(new(SizeType.Percent,100));panel.RowStyles.Add(new(SizeType.AutoSize));d.Controls.Add(panel);
        var hint=Description("Write text or a web link. You can review the result in Activity.");panel.Controls.Add(hint,0,0);var entry=new TextBox{Multiline=true,Dock=DockStyle.Fill,MaxLength=8192,ScrollBars=ScrollBars.Vertical,Margin=new(0,8,0,12)};panel.Controls.Add(entry,0,1);var go=ActionButton("Send text or link",()=>d.DialogResult=DialogResult.OK,true);panel.Controls.Add(go,0,2);panel.SizeChanged+=(_,_)=>hint.MaximumSize=new(Math.Max(120,panel.ClientSize.Width-panel.Padding.Horizontal),0);
        if(d.ShowDialog(this)!=DialogResult.OK)return;await StageText(target.Id,entry.Text);
    }
    async void SendClipboard(){if(sendTarget.SelectedItem is SendPhone target)await StageText(target.Id,Clipboard.ContainsText()?Clipboard.GetText():"");}
    async Task StageText(string recipient,string value){try{value=value.Trim();if(string.IsNullOrEmpty(value))throw new ArgumentException("Enter or copy some text first.");var kind=Uri.TryCreate(value,UriKind.Absolute,out var u)&&u.Scheme is "http" or "https"?"link":"text";await outbox!.Stage(recipient,null,value,kind);RefreshSent();}catch(Exception e){MessageBox.Show(this,e.Message,"Could not send");}}
    static string FriendlyState(string value)=>value switch{"queued"=>"Waiting to send","uploading" or "sending"=>"Transferring","retrying"=>"Waiting for device","completed"=>"Completed","saved"=>"Saved","submitted"=>"Sent to printer","delivered"=>"Delivered","running"=>"Performing action","uncertain"=>"Check receiver / printer","cancelled" or "canceled"=>"Canceled","failed"=>"Needs attention",_=>System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(value)};
    int Px(int value)=>(int)Math.Round(value*DeviceDpi/96d);
    static string Bytes(long size)=>size>=1048576?$"{size/1048576d:0.#} MB":size>=1024?$"{size/1024d:0.#} KB":$"{size} B";
}
