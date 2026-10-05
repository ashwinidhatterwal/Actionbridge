import pathlib,sys,tempfile,socket,traceback
sys.path.insert(0,str(pathlib.Path(__file__).resolve().parents[1]/'desktop'))
import app
from gi.repository import GLib,Gtk,Gdk
app.HOST_PATH=str(pathlib.Path(sys.argv[1]).resolve());root=tempfile.TemporaryDirectory(prefix='ab-gui-');s=socket.socket();s.bind(('127.0.0.1',0));port=s.getsockname()[1];s.close();a=app.App(['--offline-test','--no-discovery','--data-dir',root.name,'--port',str(port)]);checks=[];failed=[]
def check(v,m):
 assert v,m
 checks.append(m);print('PASS:',m,flush=True)
def run_checks():
 try:
  check('Ready for local' in a.local_status.get_text(),'desktop connects to real receiver')
  check(a.notebook.get_n_pages()==4,'four native desktop tabs rendered')
  a.copy_text('Ubuntu clipboard क',lambda error:check(not error,'native X11 clipboard verified'))
  for page in range(4):a.notebook.set_current_page(page)
  a.notebook.set_current_page(0)
  a.update(dict(a.snapshot,phones=[{'id':'testphone','name':'My phone'}]))
  check(a.destination.get_active_id()=='testphone','paired phone target visible')
  a.update(dict(a.snapshot,computers=[{'id':'computer:example','name':'Office computer','connection':'Nearby','status':'Connected'}]))
  check('computer:example' in [target[0] for target in a.targets],'saved computer available on Send page')
  a.destination.set_active_id('computer:example');check(a.destination.get_active_id()=='computer:example','computer target can be selected')
  a.hide_window();check(not a.window.get_visible() and a.backend.process.poll() is None,'closing window preserves running receiver');a.activate();check(a.window.get_visible(),'launcher activation restores existing window')
  import qrcode
  qr=qrcode.make('abremote:test');qr.save(str(pathlib.Path(root.name)/'qr.png'));check(qr.size[0]>0,'pairing QR renderer works')
  a.notebook.set_current_page(1)
  GLib.timeout_add(500,capture)
 except Exception:failed.append(traceback.format_exc());a.quit_app()
 return False

def capture():
 try:
  w=a.window.get_window();pix=Gdk.pixbuf_get_from_window(w,0,0,w.get_width(),w.get_height());pix.savev(sys.argv[2],'png',[],[]);check(True,'actual GTK window screenshot captured')
 except Exception:failed.append(traceback.format_exc())
 a.quit_app();return False
GLib.timeout_add_seconds(4,run_checks);a.run(['actionbridge']);root.cleanup()
if failed:raise Exception('\n'.join(failed))
print(len(checks),'GUI checks passed.')
