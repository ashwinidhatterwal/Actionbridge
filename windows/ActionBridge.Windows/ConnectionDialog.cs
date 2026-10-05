using ActionBridge.Core;
using QRCoder;
namespace ActionBridge.Windows;
public sealed partial class BridgeForm {
    void ShowConnectionDialog(int initial=0,bool preview=false){
        if(!preview&&(computers==null||server==null)){MessageBox.Show(this,"Receiver is still starting. Try again shortly.");return;}
        using var dialog=new Form{Text="Add a device",ClientSize=new(640,540),MinimumSize=new(520,460),Font=Font,AutoScaleMode=AutoScaleMode.Dpi,StartPosition=FormStartPosition.CenterParent,BackColor=BackColor,MinimizeBox=false,MaximizeBox=false};
        var shell=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new(24)};shell.RowStyles.Add(new(SizeType.AutoSize));shell.RowStyles.Add(new(SizeType.AutoSize));shell.RowStyles.Add(new(SizeType.Percent,100));dialog.Controls.Add(shell);
        shell.Controls.Add(Title("Connect your devices",20),0,0);shell.Controls.Add(Description("Open ActionBridge on the other device. Choose how to connect."),0,1);
        var pages=new TabControl{Dock=DockStyle.Fill,Padding=new(14,10)};shell.Controls.Add(pages,0,2);
        var nearby=new TabPage("Nearby"){Padding=new(16),BackColor=Color.White};var qrPage=new TabPage("QR code"){Padding=new(16),BackColor=Color.White};var manual=new TabPage("Enter code / IP"){Padding=new(16),BackColor=Color.White};pages.TabPages.AddRange([nearby,qrPage,manual]);pages.SelectedIndex=initial;
        TableLayoutPanel Stack(TabPage page,int rows){var panel=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=rows};for(int i=0;i<rows-1;i++)panel.RowStyles.Add(new(SizeType.AutoSize));panel.RowStyles.Add(new(SizeType.Percent,100));page.Controls.Add(panel);return panel;}
        var near=Stack(nearby,3);var hint=Description("Phones and computers on the same Wi-Fi appear here.\nKeep ActionBridge open on your phone.");near.Controls.Add(hint,0,0);
        var controls=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Top,WrapContents=true};near.Controls.Add(controls,0,1);
        var list=new ListBox{Dock=DockStyle.Fill,IntegralHeight=false,BorderStyle=BorderStyle.FixedSingle};near.Controls.Add(list,0,2);
        using var cancel=new CancellationTokenSource();
        var connect=ActionButton("Connect selected",()=>{},true);connect.Enabled=false;bool scanning=false;
        async Task Scan(){if(scanning)return;scanning=true;connect.Enabled=false;list.Items.Clear();hint.Text="Looking for nearby phones and computers…";
            try{var phones=NearbyPhones.Find(cancel.Token);var pcs=computers!.Discover(cancel.Token);await Task.WhenAll(phones,pcs);if(dialog.IsDisposed)return;
                foreach(var phone in await phones)list.Items.Add(phone);foreach(var pc in await pcs)list.Items.Add(new DiscoveredComputer(pc));
                hint.Text=list.Items.Count>0?"Select a device, then Connect. Approve the request on both devices.":"No devices found. Open ActionBridge on your phone, use the same Wi-Fi,\nthen Refresh. You can also use QR code or Enter code / IP.";
            }catch(OperationCanceledException){}catch(Exception e){if(!dialog.IsDisposed)hint.Text="Discovery unavailable: "+e.Message;}finally{scanning=false;}}
        controls.Controls.Add(ActionButton("Refresh",async()=>await Scan()));controls.Controls.Add(connect);list.SelectedIndexChanged+=(_,_)=>connect.Enabled=list.SelectedItem!=null&&!scanning;
        connect.Click+=async(_,_)=>{connect.Enabled=false;try{
            if(list.SelectedItem is NearbyPhone phone){await NearbyPhones.Invite(phone,server!.Announcement(),cancel.Token);hint.Text="Invitation sent to "+phone.Name+". Tap Connect on your phone,\nthen Allow on this computer. Select this computer on your phone to receive.";}
            else if(list.SelectedItem is DiscoveredComputer pc){await computers!.AddLocal(pc.Endpoint.Host,pc.Endpoint.Port,cancel.Token);RefreshSendTargets();dialog.Close();}
        }catch(Exception e){if(!dialog.IsDisposed){hint.Text=e.Message;connect.Enabled=true;}}};
        var manualPanel=Stack(manual,4);manualPanel.Controls.Add(Description("Computer pairing code or local IP address"),0,0);var entry=new TextBox{Dock=DockStyle.Top,PlaceholderText="abremote:… or 192.168.1.10",Margin=new(0,8,0,16)};manualPanel.Controls.Add(entry,0,1);var result=Description("For a phone, use Nearby or let it scan this computer’s QR code.");manualPanel.Controls.Add(result,0,2);
        var add=ActionButton("Connect computer",()=>{},true);add.Anchor=AnchorStyles.Top|AnchorStyles.Left;manualPanel.Controls.Add(add,0,3);add.Click+=async(_,_)=>{add.Enabled=false;try{var value=entry.Text.Trim();if(value.StartsWith("abremote:"))computers!.AddRemote(value);else await computers!.AddLocal(value,ct:cancel.Token);RefreshSendTargets();dialog.Close();}catch(Exception e){if(!dialog.IsDisposed){result.Text=e.Message;add.Enabled=true;}}};
        var qrPanel=Stack(qrPage,3);var qrHint=Description("On your phone: + Add → Scan QR code.\nOn another computer: paste the copied code into Enter code / IP.");qrPanel.Controls.Add(qrHint,0,0);var copy=ActionButton("Copy private pairing code",()=>{if(remote?.Config!=null){Clipboard.SetText(remote.PairingCode());qrHint.Text="Pairing code copied. Keep it private: it grants access to this computer.";}});copy.Enabled=false;qrPanel.Controls.Add(copy,0,1);var picture=new PictureBox{Dock=DockStyle.Fill,SizeMode=PictureBoxSizeMode.Zoom};qrPanel.Controls.Add(picture,0,2);bool loading=false;
        async Task LoadQR(){if(loading||picture.Image!=null)return;loading=true;try{await EnsureRemote(true);if(dialog.IsDisposed)return;if(remote?.Config==null){qrHint.Text="Internet setup unavailable. Nearby connections still work.";return;}using var generator=new QRCodeGenerator();using var data=generator.CreateQrCode(remote.PairingCode(),QRCodeGenerator.ECCLevel.M);using var qr=new QRCode(data);picture.Image=qr.GetGraphic(6);copy.Enabled=true;}catch(Exception e){if(!dialog.IsDisposed)qrHint.Text=e.Message;}finally{loading=false;}}
        pages.SelectedIndexChanged+=async(_,_)=>{if(pages.SelectedTab==qrPage)await LoadQR();};dialog.Shown+=async(_,_)=>{if(preview)return;if(initial==1)await LoadQR();else if(initial==0)await Scan();};
        dialog.FormClosed+=(_,_)=>{cancel.Cancel();picture.Image?.Dispose();};dialog.ShowDialog(this);
    }
    sealed record DiscoveredComputer(ComputerEndpoint Endpoint){public override string ToString()=>Endpoint.Name+" · Computer · "+Endpoint.Host;}
}
