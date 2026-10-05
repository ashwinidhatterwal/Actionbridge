#!/usr/bin/python3
import os,sys,pathlib,json,io,urllib.parse,datetime
os.umask(0o077)
import gi
gi.require_version('Gtk','3.0')
from gi.repository import Gtk,Gdk,GdkPixbuf,Gio,GLib,Pango
from bridge import Backend
BASE=pathlib.Path(__file__).resolve().parent
HOST_PATH=str(BASE/'ActionBridge.Host')
APP_ID='app.actionbridge.Ubuntu'
CSS=b'''
window { background: #f4f8f8; }
headerbar { background: #ffffff; }
label.title { font-size: 23px; font-weight: bold; color: #127e76; }
label.subtitle { color: #586a71; }
button { border-radius: 8px; padding: 8px 14px; }
button.suggested-action { background: #127e76; color: white; }
notebook > stack { background: #ffffff; }
treeview { background: #ffffff; }
'''
def idle(fn):
 def run():fn();return False
 GLib.idle_add(run)
def button(text,fn,primary=False):
 b=Gtk.Button(label=text);b.connect('clicked',lambda *_:fn())
 if primary:b.get_style_context().add_class('suggested-action')
 return b
def label(text,style=None):
 l=Gtk.Label(label=text,xalign=0);l.set_line_wrap(True)
 if style:l.get_style_context().add_class(style)
 return l
def column():
 b=Gtk.Box(orientation=Gtk.Orientation.VERTICAL,spacing=14);b.set_border_width(20);return b
class App(Gtk.Application):
 def __init__(self,test_arguments=()):
  super().__init__(application_id=APP_ID,flags=Gio.ApplicationFlags.HANDLES_COMMAND_LINE)
  self.window=None;self.backend=None;self.first=True;self.test_arguments=test_arguments;self.refresh_pending=False;self.staging=False;self.snapshot={};self.targets=[];self.rows={};self.dialogs=[];self.refresh_source=None;self.closed=False
 def do_startup(self):
  Gtk.Application.do_startup(self);self.hold()
  provider=Gtk.CssProvider();provider.load_from_data(CSS);Gtk.StyleContext.add_provider_for_screen(Gdk.Screen.get_default(),provider,Gtk.STYLE_PROVIDER_PRIORITY_APPLICATION)
 def do_command_line(self,command):
  args=command.get_arguments()
  if '--quit' in args:self.quit_app();return 0
  background='--background' in args and self.first
  self.activate()
  if background:self.window.hide()
  self.first=False;return 0
 def do_activate(self):
  if self.window:self.window.present();return
  self.build_window();self.start_backend();self.refresh_source=GLib.timeout_add_seconds(2,self.poll)
 def build_window(self):
  self.window=Gtk.ApplicationWindow(application=self,title='ActionBridge · Ubuntu');self.window.set_default_size(1020,720);self.window.set_size_request(780,540);self.window.connect('delete-event',lambda *_:self.hide_window())
  icon=BASE/'actionbridge.png'
  if icon.exists():self.window.set_icon_from_file(str(icon))
  header=Gtk.HeaderBar(title='ActionBridge',subtitle='Your devices, connected · 0.6.0',show_close_button=True);self.window.set_titlebar(header)
  self.pair_button=button('Connect device',self.pair,True);header.pack_end(self.pair_button)
  menu=Gtk.Menu();item=Gtk.MenuItem(label='Quit ActionBridge');item.connect('activate',lambda *_:self.quit_app());menu.append(item);menu.show_all();hamburger=Gtk.MenuButton();hamburger.set_image(Gtk.Image.new_from_icon_name('open-menu-symbolic',Gtk.IconSize.BUTTON));hamburger.set_popup(menu);header.pack_end(hamburger)
  body=Gtk.Box(orientation=Gtk.Orientation.VERTICAL,spacing=8);self.window.add(body)
  top=Gtk.Box(orientation=Gtk.Orientation.VERTICAL,spacing=4);top.set_border_width(18);body.pack_start(top,False,False,0)
  self.local_status=label('Starting secure receiver…');self.remote_status=label('Internet connection is starting…','subtitle');top.pack_start(self.local_status,False,False,0);top.pack_start(self.remote_status,False,False,0)
  notebook=Gtk.Notebook();body.pack_start(notebook,True,True,0);self.notebook=notebook
  activity=column();notebook.append_page(activity,Gtk.Label(label='Activity'));self.empty=label('Ready for your next file. Connect a device and send a file or action.','subtitle');activity.pack_start(self.empty,False,False,0)
  self.history,self.history_store=self.table(activity,['Time','File / action','Connection','State','Details'],[145,230,125,110,320]);self.history.connect('row-activated',lambda *_:self.open_selected())
  row=Gtk.Box(spacing=8);activity.pack_start(row,False,False,0);row.pack_start(button('Received files',lambda:self.call('openFolder')),False,False,0);row.pack_start(button('Cancel selected transfer',self.cancel_job),False,False,0)
  sending=column();notebook.append_page(sending,Gtk.Label(label='Send'));sending.pack_start(label('Send files, text or a link','title'),False,False,0)
  self.destination=Gtk.ComboBoxText();sending.pack_start(self.destination,False,False,0)
  send_row=Gtk.Box(spacing=8);sending.pack_start(send_row,False,False,0);self.file_button=button('Choose files',self.choose_files,True);send_row.pack_start(self.file_button,False,False,0);send_row.pack_start(button('Send text / link',self.send_text),False,False,0);send_row.pack_start(button('Send clipboard',self.send_clipboard),False,False,0);send_row.pack_start(button('Add computer',self.add_computer),False,False,0)
  sending.pack_start(label('Computers receive while ActionBridge is running. For a phone, keep its main screen open and select this computer.','subtitle'),False,False,0)
  self.sent,self.sent_store=self.table(sending,['Time','Item','State','Progress'],[145,330,120,190]);sending.drag_dest_set(Gtk.DestDefaults.ALL,[],Gdk.DragAction.COPY);sending.drag_dest_add_uri_targets();sending.connect('drag-data-received',self.drop_files)
  sending.pack_start(button('Cancel selected outgoing file',self.cancel_outgoing),False,False,0)
  devices=column();notebook.append_page(devices,Gtk.Label(label='Devices'));devices.pack_start(label('Your paired devices','title'),False,False,0);self.phone_list,self.phone_store=self.table(devices,['Device','Connection'],[400,250]);self.phone_ids=[]
  devices.pack_start(button('Remove selected device',self.remove_phone),False,False,0);devices.pack_start(button('Find nearby computers',self.discover_computers),False,False,0);devices.pack_start(button('Revoke remote device access',self.revoke_remote),False,False,0);devices.pack_start(label('Keep the pairing QR private. The remote QR slot supports one active phone or computer at a time.','subtitle'),False,False,0)
  preferences=column();notebook.append_page(preferences,Gtk.Label(label='Preferences'));preferences.pack_start(label('Your workspace','title'),False,False,0)
  self.startup=Gtk.CheckButton(label='Start receiving when I sign in');self.startup.set_active(self.autostart_path().exists());self.startup.connect('toggled',self.toggle_startup);preferences.pack_start(self.startup,False,False,0)
  preferences.pack_start(label('Closing this window keeps receiving. Open the desktop launcher to return. Use the menu → Quit to stop the companion.','subtitle'),False,False,0)
  preferences.pack_start(label('Printing uses printers configured in Ubuntu Settings. Phone print options include paper, copies, orientation, color, scaling, duplex and page ranges when supported. Submitted means accepted by CUPS, not physically printed.','subtitle'),False,False,0)
  preferences.pack_start(label('On Wayland, received text may need a click on Copy to clipboard. This keeps clipboard access tied to your desktop action.','subtitle'),False,False,0)
  settings_row=Gtk.Box(spacing=8);preferences.pack_start(settings_row,False,False,0);settings_row.pack_start(button('Check printers',self.check_printers),False,False,0);settings_row.pack_start(button('Restart receiver',self.restart),False,False,0);settings_row.pack_start(button('Privacy and licenses',self.privacy),False,False,0)
  self.feedback=label('Your existing Android v0.5 app works with this companion.','subtitle');self.feedback.set_margin_start(20);self.feedback.set_margin_end(20);self.feedback.set_margin_bottom(12);body.pack_start(self.feedback,False,False,0);self.window.show_all()
 def table(self,parent,names,widths):
  store=Gtk.ListStore(*([str]*len(names)));view=Gtk.TreeView(model=store);view.set_headers_visible(True)
  for i,(name,width) in enumerate(zip(names,widths)):
   renderer=Gtk.CellRendererText();renderer.set_property('ellipsize',Pango.EllipsizeMode.END);c=Gtk.TreeViewColumn(name,renderer,text=i);c.set_resizable(True);c.set_min_width(70);c.set_fixed_width(width);c.set_sizing(Gtk.TreeViewColumnSizing.FIXED);view.append_column(c)
  scroll=Gtk.ScrolledWindow();scroll.set_policy(Gtk.PolicyType.AUTOMATIC,Gtk.PolicyType.AUTOMATIC);scroll.add(view);parent.pack_start(scroll,True,True,0);return view,store
 def hide_window(self):self.window.hide();return True
 def start_backend(self):
  try:self.backend=Backend(HOST_PATH,idle,self.event,self.backend_exit,self.test_arguments)
  except Exception as e:self.backend_exit(str(e))
 def restart(self):
  if self.staging:self.error('Wait for file preparation to finish before restarting.');return
  if self.backend:self.backend.stop()
  self.refresh_pending=False;self.local_status.set_text('Restarting receiver…');self.start_backend()
 def backend_exit(self,message):self.local_status.set_text(message);self.feedback.set_text('Use Preferences → Restart receiver after resolving the error.');self.pair_button.set_sensitive(False)
 def call(self,method,values=None,callback=None,timeout=60):
  if not self.backend:self.error('Receiver is unavailable. Restart it from Preferences.');return
  def complete(result,error):
   if self.closed:return
   if error:self.error(error)
   if callback:callback(result,error)
   elif not error:self.feedback.set_text('Done.');self.poll()
  return self.backend.call(method,values,complete,timeout)
 def poll(self):
  if not self.backend or self.backend.process.poll() is not None or self.refresh_pending:return True
  self.refresh_pending=True
  def done(value,error):
   self.refresh_pending=False
   if error:self.feedback.set_text(error)
   elif value:self.update(value)
  self.backend.call('snapshot',callback=done,timeout=8);return True
 def update(self,value):
  previous=self.snapshot;self.snapshot=value;self.remote_status.set_text((value.get('remoteStatus') or 'Internet unavailable').replace('Connect phone','Connect device'));self.pair_button.set_sensitive(True)
  def when(raw):
   try:return datetime.datetime.fromisoformat(raw.replace('Z','+00:00')).astimezone().strftime('%d %b %H:%M')
   except Exception:return ''
  if value.get('jobs')!=previous.get('jobs'):
   selected=self.selected_index(self.history);selected_id=self.job_ids[selected] if selected is not None and selected<len(getattr(self,'job_ids',[])) else None
   self.history_store.clear();self.job_ids=[];self.rows={}
   for j in value.get('jobs',[]):
    r=j['request'];self.job_ids.append(r['id']);self.rows[r['id']]=j;self.history_store.append([when(j['updated']),r['name']+' · '+r['action'],'Internet' if j['clientId'].startswith('remote:') else 'Local',j['state'],j.get('message') or f"{j['offset']:,} / {r['size']:,} bytes"])
   self.empty.set_visible(not self.job_ids);self.restore_selection(self.history,self.job_ids,selected_id)
  if value.get('outgoing')!=previous.get('outgoing'):
   selected=self.selected_index(self.sent);selected_id=self.out_ids[selected] if selected is not None and selected<len(getattr(self,'out_ids',[])) else None
   self.sent_store.clear();self.out_ids=[]
   for item in value.get('outgoing',[]):self.out_ids.append(item['id']);self.sent_store.append([when(item['updated']),item['name'],item['state'],f"{item.get('offset',0):,} / {item['size']:,} bytes"])
   self.restore_selection(self.sent,self.out_ids,selected_id)
  targets=[(p['id'],p['name']+' · '+p['connection']) for p in value.get('computers',[])]
  if value.get('remoteReady'):targets.append((value['remoteRecipient'],'Device paired by QR · Internet or nearby'))
  targets.extend((p['id'],p['name']+' · Local pairing') for p in value.get('phones',[]))
  if targets!=self.targets:
   old=self.destination.get_active_id();self.targets=targets;self.destination.remove_all();self.phone_store.clear();self.phone_ids=[]
   for id,name in targets:
    self.destination.append(id,name);connection=next((p['connection'] for p in value.get('computers',[]) if p['id']==id),'Internet' if id.startswith('remote:') else 'Local');self.phone_store.append([name,connection]);self.phone_ids.append(id)
   if not self.destination.set_active_id(old or '') and targets:self.destination.set_active(0)
 def add_computer(self):
  d=Gtk.Dialog(title='Add a computer',transient_for=self.window,modal=True);d.add_buttons('Cancel',Gtk.ResponseType.CANCEL,'Connect',Gtk.ResponseType.OK);box=d.get_content_area();box.set_border_width(18);box.set_spacing(12)
  box.pack_start(label('Nearby: enter the receiving computer’s local IP. Internet: paste its private ActionBridge pairing code. The destination stays saved.'),False,False,0)
  mode=Gtk.ComboBoxText();mode.append('nearby','Nearby IP address');mode.append('internet','Internet pairing code');mode.set_active(0);box.pack_start(mode,False,False,0);entry=Gtk.Entry();entry.set_placeholder_text('192.168.1.20 or abremote:…');entry.set_width_chars(50);box.pack_start(entry,False,False,0);d.show_all()
  if d.run()==Gtk.ResponseType.OK:
   value=entry.get_text().strip();data={'code':value} if mode.get_active_id()=='internet' else {'host':value};self.feedback.set_text('Connecting… Approve the request on the other computer.');self.call('addComputer',data,timeout=90)
  d.destroy()
 def discover_computers(self):
  self.feedback.set_text('Looking for nearby computers…')
  def done(value,error):
   if error:return
   if not value:self.error('No nearby computers found. Use Add computer and enter a local IP address.');return
   d=Gtk.Dialog(title='Nearby computers',transient_for=self.window,modal=True);d.add_buttons('Cancel',Gtk.ResponseType.CANCEL,'Connect',Gtk.ResponseType.OK);box=d.get_content_area();box.set_border_width(18);box.pack_start(label('Approve only a computer you trust on this network.'),False,False,10);choices=Gtk.ComboBoxText()
   for i,p in enumerate(value):choices.append(str(i),p['name']+' · '+p['host'])
   choices.set_active(0);box.pack_start(choices,False,False,10);d.show_all()
   if d.run()==Gtk.ResponseType.OK:
    peer=value[int(choices.get_active_id())];self.call('addComputer',{'host':peer['host'],'port':peer['port']},timeout=90)
   d.destroy()
  self.call('discoverComputers',callback=done,timeout=10)
 def selected_index(self,view):
  model,it=view.get_selection().get_selected();return model.get_path(it).get_indices()[0] if it is not None else None
 def restore_selection(self,view,ids,value):
  if value in ids:view.get_selection().select_path(Gtk.TreePath.new_from_indices([ids.index(value)]))
 def recipient(self):
  id=self.destination.get_active_id()
  if not id:self.error('Add a computer or connect a phone first.');return None
  return id
 def choose_files(self):
  if not self.recipient():return
  dialog=Gtk.FileChooserDialog(title='Send files',transient_for=self.window,action=Gtk.FileChooserAction.OPEN);dialog.add_buttons('Cancel',Gtk.ResponseType.CANCEL,'Choose',Gtk.ResponseType.OK);dialog.set_select_multiple(True)
  if dialog.run()==Gtk.ResponseType.OK:self.queue_files(dialog.get_filenames())
  dialog.destroy()
 def queue_files(self,paths):
  recipient=self.recipient()
  if not recipient or self.staging:return
  if not 1<=len(paths)<=20:self.error('Choose 1–20 files at once.');return
  self.staging=True;self.file_button.set_sensitive(False);self.feedback.set_text('Preparing private file copies…')
  def done(value,error):self.staging=False;self.file_button.set_sensitive(True);self.feedback.set_text(error or 'Queued. Keep ActionBridge running on the receiving device.');self.poll()
  self.call('sendFiles',{'recipient':recipient,'paths':paths},done,3600)
 def drop_files(self,widget,context,x,y,data,info,time):
  paths=[]
  for uri in data.get_uris() or []:
   u=urllib.parse.urlsplit(uri)
   if u.scheme=='file' and u.netloc in ('','localhost'):paths.append(urllib.parse.unquote(u.path))
  self.queue_files(paths);Gtk.drag_finish(context,bool(paths),False,time)
 def send_value(self,text):
  recipient=self.recipient()
  if not recipient:return
  if not text or len(text.encode('utf-8'))>8192:self.error('Enter text up to 8 KB.');return
  u=urllib.parse.urlsplit(text);kind='link' if u.scheme in ('http','https') and u.netloc else 'text';self.call('sendText',{'recipient':recipient,'text':text,'kind':kind})
 def send_clipboard(self):Gtk.Clipboard.get(Gdk.SELECTION_CLIPBOARD).request_text(lambda clipboard,text,*_:self.send_value(text or ''))
 def send_text(self):
  if not self.recipient():return
  d=Gtk.Dialog(title='Send text or link',transient_for=self.window,modal=True);d.set_default_size(520,280);d.add_buttons('Cancel',Gtk.ResponseType.CANCEL,'Send',Gtk.ResponseType.OK);text=Gtk.TextView();text.set_wrap_mode(Gtk.WrapMode.WORD_CHAR);scroll=Gtk.ScrolledWindow();scroll.add(text);d.get_content_area().pack_start(scroll,True,True,10);d.show_all()
  if d.run()==Gtk.ResponseType.OK:b=text.get_buffer();self.send_value(b.get_text(b.get_start_iter(),b.get_end_iter(),True).strip())
  d.destroy()
 def cancel_outgoing(self):
  i=self.selected_index(self.sent)
  if i is not None:self.call('cancelOutgoing',{'id':self.out_ids[i]})
 def cancel_job(self):
  i=self.selected_index(self.history)
  if i is not None:self.call('cancelJob',{'id':self.job_ids[i]})
 def open_selected(self):
  i=self.selected_index(self.history)
  if i is not None:self.call('openJob',{'id':self.job_ids[i]})
 def remove_phone(self):
  i=self.selected_index(self.phone_list)
  if i is None:return
  id=self.phone_ids[i]
  if id.startswith('remote:'):self.revoke_remote();return
  if id.startswith('computer:'):
   if self.confirm('Remove this computer and cancel its waiting files?'):self.call('removeComputer',{'id':id})
   return
  if self.confirm('Remove this device’s local access?'):self.call('revoke',{'id':id})
 def revoke_remote(self):
  if self.confirm('Revoke the current remote QR and device access? This requires internet.'):self.call('revokeRemote')
 def pair(self):self.feedback.set_text('Preparing your pairing code…');self.call('pairing',callback=lambda result,error:self.show_pairing(result['code']) if not error else None)
 def show_pairing(self,code):
  import qrcode
  qr=qrcode.QRCode(error_correction=qrcode.constants.ERROR_CORRECT_M,box_size=5,border=4);qr.add_data(code);qr.make(fit=True);buffer=io.BytesIO();qr.make_image(fill_color='black',back_color='white').save(buffer,format='PNG');loader=GdkPixbuf.PixbufLoader.new_with_type('png');loader.write(buffer.getvalue());loader.close();pix=loader.get_pixbuf().scale_simple(390,390,GdkPixbuf.InterpType.NEAREST)
  d=Gtk.Dialog(title='Connect a device',transient_for=self.window,modal=True);d.add_button('Done',Gtk.ResponseType.CLOSE);box=d.get_content_area();box.set_border_width(18);box.set_spacing(12);box.pack_start(label('Phone: Add computer → Scan QR code.\nComputer: Send → Add computer → paste the copied code.\nKeep this code private: it grants file and action access.'),False,False,0);box.pack_start(Gtk.Image.new_from_pixbuf(pix),True,True,0)
  def copy():self.copy_text(code,lambda error:self.feedback.set_text(error or 'Pairing code copied. Keep it private.'))
  box.pack_start(button('Copy code for manual entry',copy),False,False,0);d.show_all();d.run();d.destroy()
 def event(self,m):
  name=m['event'];data=m.get('data',{});id=m.get('uiId')
  if name=='ready':self.local_status.set_text(data['name']+' · Ready for local transfers');self.pair_button.set_sensitive(True);self.poll()
  elif name=='remote':self.remote_status.set_text(data.get('message',''))
  elif name=='changed':self.poll()
  elif name=='fatal':self.backend_exit(data.get('message','Receiver failed'))
  elif name=='notification':
   if not self.window.get_visible():
    n=Gio.Notification.new('ActionBridge');n.set_body('Device activity updated. Open ActionBridge to view details.');self.send_notification('activity',n)
  elif name=='approval':self.prompt_reply(id,'Allow this device?',data['name']+' ('+data['ip']+') wants local access.\nAllow only if you requested this connection.','Allow',lambda:{'allowed':True},{'allowed':False})
  elif name=='clipboard':
   text=data['text']
   if os.environ.get('XDG_SESSION_TYPE')=='wayland' or os.environ.get('WAYLAND_DISPLAY'):
    self.prompt_clipboard(id,text)
   else:self.copy_text(text,lambda error:self.backend.reply(id,{'copied':not error},error))
  elif name=='open':
   try:
    target=data['target'];u=urllib.parse.urlsplit(target);uri=target if u.scheme in ('http','https') else Gio.File.new_for_path(target).get_uri();context=self.window.get_display().get_app_launch_context();opened=Gio.AppInfo.launch_default_for_uri(uri,context);self.backend.reply(id,{'opened':bool(opened)})
   except Exception:self.backend.reply(id,error='No installed application could open this item. It remains saved.')
 def copy_text(self,text,done):
  clipboard=Gtk.Clipboard.get(Gdk.SELECTION_CLIPBOARD);clipboard.set_text(text,-1);clipboard.store();clipboard.request_text(lambda _,actual,*args:done(None if actual==text else 'Clipboard access was not confirmed. Focus ActionBridge and try again.'))
 def prompt_clipboard(self,id,text):
  d=Gtk.Dialog(title='Text received from phone',transient_for=self.window,modal=True);d.add_buttons('Decline',Gtk.ResponseType.CANCEL,'Copy to clipboard',Gtk.ResponseType.OK);d.set_default_size(500,260);box=d.get_content_area();box.set_border_width(16);box.pack_start(label('Click Copy to clipboard, then paste in another application.'),False,False,8);v=Gtk.TextView();v.set_editable(False);v.set_wrap_mode(Gtk.WrapMode.WORD_CHAR);v.get_buffer().set_text(text);s=Gtk.ScrolledWindow();s.add(v);box.pack_start(s,True,True,0)
  answered=[False]
  def response(dialog,value):
   if answered[0]:return
   answered[0]=True
   if value==Gtk.ResponseType.OK:self.copy_text(text,lambda error:self.backend.reply(id,{'copied':not error},error))
   else:self.backend.reply(id,error='Clipboard copy declined or expired.')
   dialog.destroy()
  d.connect('response',response);d.show_all();d.present();GLib.timeout_add_seconds(50,lambda:self.expire_dialog(d,answered))
 def prompt_reply(self,id,title,message,ok,result,cancel):
  d=Gtk.MessageDialog(transient_for=self.window,modal=True,message_type=Gtk.MessageType.QUESTION,buttons=Gtk.ButtonsType.NONE,text=title);d.format_secondary_text(message);d.add_buttons('Decline',Gtk.ResponseType.CANCEL,ok,Gtk.ResponseType.OK);answered=[False]
  def response(dialog,value):
   if answered[0]:return
   answered[0]=True;self.backend.reply(id,result() if value==Gtk.ResponseType.OK else cancel);dialog.destroy()
  d.connect('response',response);d.show_all();d.present();GLib.timeout_add_seconds(50,lambda:self.expire_dialog(d,answered))
 def expire_dialog(self,d,answered):
  if not answered[0]:d.response(Gtk.ResponseType.CANCEL)
  return False
 def error(self,message):self.feedback.set_text(message)
 def confirm(self,message):
  d=Gtk.MessageDialog(transient_for=self.window,modal=True,message_type=Gtk.MessageType.QUESTION,buttons=Gtk.ButtonsType.YES_NO,text=message);answer=d.run()==Gtk.ResponseType.YES;d.destroy();return answer
 def check_printers(self):self.call('printers',callback=lambda value,error:self.info('Printers','\n'.join(p['name']+(' · default' if p['isDefault'] else '') for p in value) or 'No printers configured. Add one in Ubuntu Settings.') if not error else None)
 def info(self,title,message):
  d=Gtk.MessageDialog(transient_for=self.window,modal=True,message_type=Gtk.MessageType.INFO,buttons=Gtk.ButtonsType.CLOSE,text=title);d.format_secondary_text(message);d.run();d.destroy()
 def privacy(self):
  text=(BASE/'PRIVACY.txt').read_text() if (BASE/'PRIVACY.txt').exists() else 'No advertising or analytics. Transfers use your approved local/remote connection.'
  d=Gtk.Dialog(title='Privacy and licenses',transient_for=self.window,modal=True);d.set_default_size(620,460);d.add_button('Close',Gtk.ResponseType.CLOSE);v=Gtk.TextView();v.set_editable(False);v.set_wrap_mode(Gtk.WrapMode.WORD_CHAR);v.get_buffer().set_text(text);s=Gtk.ScrolledWindow();s.add(v);d.get_content_area().pack_start(s,True,True,8);d.show_all();d.run();d.destroy()
 def autostart_path(self):
  base=os.environ.get('XDG_CONFIG_HOME','');return (pathlib.Path(base) if os.path.isabs(base) else pathlib.Path.home()/'.config')/'autostart'/'app.actionbridge.Ubuntu.desktop'
 def toggle_startup(self,widget):
  path=self.autostart_path()
  try:
   path.parent.mkdir(parents=True,exist_ok=True)
   if widget.get_active():path.write_text('[Desktop Entry]\nType=Application\nName=ActionBridge\nExec=/usr/bin/actionbridge --background\nIcon=app.actionbridge.Ubuntu\nTerminal=false\nX-GNOME-Autostart-enabled=true\n')
   else:path.unlink(missing_ok=True)
  except OSError as e:self.error('Could not change startup: '+str(e))
 def quit_app(self):
  self.closed=True
  if self.refresh_source:GLib.source_remove(self.refresh_source);self.refresh_source=None
  if self.backend:self.backend.stop();self.backend=None
  self.release();self.quit()
if __name__=='__main__':
 App().run(sys.argv)
