"""Two published Ubuntu hosts, real TLS/IPC; optional temporary production signaling room."""
import pathlib,sys,tempfile,threading,queue,json,socket,time,hashlib
sys.path.insert(0,str(pathlib.Path(__file__).resolve().parents[1]/'desktop'))
from bridge import Backend
HOST=pathlib.Path(sys.argv[1]).resolve();count=0

def check(value,message):
 global count
 assert value,message
 count+=1;print('PASS:',message,flush=True)
def port():
 s=socket.socket();s.bind(('127.0.0.1',0));p=s.getsockname()[1];s.close();return p
class Computer:
 def __init__(self,root,port):
  self.root=pathlib.Path(root);self.port=port;self.ready=threading.Event();self.backend=None
  def event(m):
   if m['event']=='ready':self.ready.set()
   if m.get('uiId'):self.backend.reply(m['uiId'],{'allowed':True,'copied':True,'opened':True})
  self.backend=Backend(str(HOST),lambda f:f(),event,lambda e:None,['--data-dir',str(root),'--offline-test','--no-discovery','--port',str(port)])
  assert self.ready.wait(15),'host failed to start'
 def call(self,method,**values):
  q=queue.Queue();self.backend.call(method,values,lambda v,e:q.put((v,e)),timeout=100);v,e=q.get(timeout=105);assert not e,e;return v
 def snapshot(self):return self.call('snapshot')
 def close(self):self.backend.stop()
def wait(fn,seconds=35):
 end=time.monotonic()+seconds
 while time.monotonic()<end:
  v=fn()
  if v:return v
  time.sleep(.2)
 raise AssertionError('transfer deadline expired')
def delivered(pc,id):return next((i for i in pc.snapshot()['outgoing'] if i['id']==id and i['state']=='delivered'),None)
def latest(pc):return pc.snapshot()['outgoing'][0]['id']
def saved(pc,id,body):
 job=next(j for j in pc.snapshot()['jobs'] if j['request']['id']==id)
 files=list((pc.root/'downloads/ActionBridge').glob(id+'-*'))
 return job['state']=='completed' and len(files)==1 and files[0].read_bytes()==body
with tempfile.TemporaryDirectory(prefix='ab-desktops-') as root:
 a=Computer(pathlib.Path(root)/'a',port());b=Computer(pathlib.Path(root)/'b',port());remote=False
 try:
  a.call('addComputer',host='127.0.0.1',port=b.port);target=a.snapshot()['computers'][0]['id'];check(target.startswith('computer:'),'native host exposes saved computer destination')
  data=('Ubuntu desktop क\n'.encode()*60000)[:800000];file=pathlib.Path(root)/'desktop.dat';file.write_bytes(data)
  a.call('sendFiles',recipient=target,paths=[str(file)]);id=latest(a);wait(lambda:delivered(a,id));check(saved(b,id,data),'two real Ubuntu hosts transfer and save over pinned local HTTPS')
  client=b.snapshot()['phones'][0]['id'];b.call('sendFiles',recipient=client,paths=[str(file)]);reverse=latest(b);wait(lambda:delivered(b,reverse));check(saved(a,reverse,data),'receiver sends file back through the same local pairing')
  a.close();a=Computer(pathlib.Path(root)/'a',a.port);check(a.snapshot()['computers'][0]['id']==target,'computer destination survives native host restart')
  b.call('sendFiles',recipient=client,paths=[str(file)]);restart=latest(b);wait(lambda:delivered(b,restart));check(saved(a,restart,data),'receiver polling resumes after sender host restart')
  a.call('removeComputer',id=target);check(not a.snapshot()['computers'],'user removal deletes saved desktop destination')
  if '--remote' in sys.argv:
   code=b.call('pairing')['code'];remote=True;a.call('addComputer',code=code);target=a.snapshot()['computers'][0]['id'];a.call('sendFiles',recipient=target,paths=[str(file)]);id=latest(a);wait(lambda:delivered(a,id),100);check(saved(b,id,data),'desktop WebRTC initiator transfers through actual Cloudflare signaling')
   snapshot=b.snapshot();b.call('sendFiles',recipient=snapshot['remoteRecipient'],paths=[str(file)]);reverse=latest(b);wait(lambda:delivered(b,reverse),100);check(saved(a,reverse,data),'reverse desktop WebRTC download saves and acknowledges through same channel')
   a.close();a=Computer(pathlib.Path(root)/'a',a.port);b.call('sendFiles',recipient=b.snapshot()['remoteRecipient'],paths=[str(file)]);again=latest(b);wait(lambda:delivered(b,again),100);check(saved(a,again,data),'internet computer pairing survives native host and helper restart')
 except Exception:
  print('Connection status:',[(p.get('connection'),p.get('status')) for p in a.snapshot().get('computers',[])],flush=True);print('Receiver status:',b.snapshot().get('remoteStatus'),flush=True);raise
 finally:
  if remote:
   try:b.call('revokeRemote')
   except Exception as e:print('Temporary remote test room cleanup failed:',type(e).__name__,flush=True);raise
  a.close();b.close()
print(count,'native computer integration checks passed.')
