import pathlib,sys,tempfile,socket,traceback,os
sys.path.insert(0,str(pathlib.Path(__file__).resolve().parents[1]/'desktop'))
import app
from gi.repository import GLib,Gtk,Gdk
app.HOST_PATH=str(pathlib.Path(sys.argv[1]).resolve());root=tempfile.TemporaryDirectory(prefix='ab-gui-');os.environ['XDG_CONFIG_HOME']=root.name+'/config';s=socket.socket();s.bind(('127.0.0.1',0));port=s.getsockname()[1];s.close();a=app.App(['--offline-test','--no-discovery','--data-dir',root.name,'--port',str(port)]);checks=[];failed=[]
def check(v,m):
 assert v,m
 checks.append(m);print('PASS:',m,flush=True)
def run_checks():
 try:
  check('Ready to receive' in a.local_status.get_text(),'desktop connects to real receiver')
  check(a.notebook.get_n_pages()==3,'Home, Activity and Settings rendered')
  a.copy_text('Ubuntu clipboard क',lambda error:check(not error,'native X11 clipboard verified'))
  for page in range(3):a.notebook.set_current_page(page)
  a.notebook.set_current_page(0)
  if a.refresh_source:GLib.source_remove(a.refresh_source);a.refresh_source=None
  check(not a.file_button.get_sensitive(),'sending disabled until a device is selected')
  a.update(dict(a.snapshot,remoteReady=True,remoteLinked=False,remoteRecipient='remote:demo'))
  check(not a.targets,'internet setup alone does not invent a paired device')
  a.update(dict(a.snapshot,phones=[{'id':'testphone','name':'My phone'}]))
  check('testphone' in a.phone_ids and not a.destination.get_active_id(),'paired device visible without automatic recipient selection')
  a.phone_list.get_selection().select_path(Gtk.TreePath.new_from_indices([0]))
  check(a.destination.get_active_id()=='testphone' and a.file_button.get_sensitive(),'choosing a device enables sending')
  a.update(dict(a.snapshot,computers=[{'id':'computer:example','name':'Office computer','connection':'Nearby','status':'Connected'}]))
  check('computer:example' in [target[0] for target in a.targets],'saved computer available on Send page')
  a.phone_list.get_selection().select_path(Gtk.TreePath.new_from_indices([a.phone_ids.index('computer:example')]))
  check(a.destination.get_active_id()=='computer:example','computer target can be selected')
  a.update(dict(a.snapshot,computers=[]))
  check(not a.destination.get_active_id() and not a.file_button.get_sensitive(),'removed recipient never silently switches to another device')
  a.update(dict(a.snapshot,computers=[{'id':'computer:example','name':'Office computer','connection':'Nearby','status':'Waiting for connection'}]))
  check(a.destination.get_active_id()=='computer:example','saved selection restored when recipient returns')
  check(a.ui_path().exists(),'selected device saved across launches')
  a.staging=True;a.update_send_controls();check(not a.file_button.get_sensitive() and not a.text_button.get_sensitive(),'duplicate send controls disabled during file preparation');a.staging=False;a.update_send_controls()
  a.hide_window();check(not a.window.get_visible() and a.backend.process.poll() is None,'closing window preserves running receiver');a.activate();check(a.window.get_visible(),'launcher activation restores existing window')
  import qrcode
  qr=qrcode.make('abremote:test');qr.save(str(pathlib.Path(root.name)/'qr.png'));check(qr.size[0]>0,'pairing QR renderer works')
  a.notebook.set_current_page(0)
  GLib.timeout_add(500,capture)
 except Exception:failed.append(traceback.format_exc());a.quit_app()
 return False

def capture():
 try:
  w=a.window.get_window();pix=Gdk.pixbuf_get_from_window(w,0,0,w.get_width(),w.get_height());pix.savev(sys.argv[2],'png',[],[]);check(True,'actual GTK window screenshot captured')
 except Exception:failed.append(traceback.format_exc());a.quit_app();return False
 a.window.resize(840,620);GLib.timeout_add(200,capture_compact);return False

def capture_compact():
 try:
  w=a.window.get_window();check(w.get_width()<=900 and w.get_height()<=700,'Home fits a compact desktop window');pix=Gdk.pixbuf_get_from_window(w,0,0,w.get_width(),w.get_height());pix.savev(str(pathlib.Path(sys.argv[2]).with_name('ubuntu-home-compact.png')),'png',[],[])
 except Exception:failed.append(traceback.format_exc())
 a.quit_app();return False
GLib.timeout_add_seconds(4,run_checks);a.run(['actionbridge']);root.cleanup()
if failed:raise Exception('\n'.join(failed))
print(len(checks),'GUI checks passed.')
