import pathlib,sys,tempfile,queue,threading,json,base64,time,socket
sys.path.insert(0,str(pathlib.Path(__file__).resolve().parents[1]/'desktop'))
from bridge import Backend
host=str(pathlib.Path(sys.argv[1]).resolve());root=tempfile.TemporaryDirectory(prefix='ab-remote-');s=socket.socket();s.bind(('127.0.0.1',0));port=s.getsockname()[1];s.close();events=[]
def launch():
 return Backend(host,lambda f:f(),lambda m:events.append(m),lambda e:None,['--no-discovery','--data-dir',root.name,'--port',str(port)])
def call(b,method):
 q=queue.Queue();b.call(method,callback=lambda v,e:q.put((v,e)),timeout=35);v,e=q.get(timeout=40);assert not e,e;return v
b=launch();revoked=False
try:
 code=call(b,'pairing')['code'];p=json.loads(base64.urlsafe_b64decode(code[9:]+'='*(-len(code[9:])%4)));assert p['v']==2 and len(p['key'])==43 and p['service']=='https://actionbridge-connect.actionbridge.workers.dev';print('PASS: live Cloudflare automatic enrollment and Android v2 QR contract',flush=True)
 b.stop();b=launch();code2=call(b,'pairing')['code'];p2=json.loads(base64.urlsafe_b64decode(code2[9:]+'='*(-len(code2[9:])%4)));assert p2==p;print('PASS: remote QR credentials remain stable across restart',flush=True)
 call(b,'revokeRemote');revoked=True;b.stop();b=launch();time.sleep(2);snap=call(b,'snapshot');assert not snap['remoteReady'] and 'off' in snap['remoteStatus'];print('PASS: revoked room remains disabled after restart',flush=True)
finally:
 if not revoked:
  try:call(b,'revokeRemote')
  except Exception:print('Remote cleanup could not be confirmed; private test data retained at',root.name);root.cleanup=lambda:None
 b.stop()
 if revoked:root.cleanup()
