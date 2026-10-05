import pathlib,sys,tempfile,threading,queue,json,ssl,urllib.request,urllib.error,uuid,hashlib,socket,time,base64,os
sys.path.insert(0,str(pathlib.Path(__file__).resolve().parents[1]/'desktop'))
from bridge import Backend
HOST=pathlib.Path(sys.argv[1]).resolve(); count=0

def check(v,m):
 global count
 assert v,m
 count+=1;print('PASS:',m,flush=True)
with tempfile.TemporaryDirectory(prefix='ab-integration-') as tmp:
 root=pathlib.Path(tmp);sock=socket.socket();sock.bind(('127.0.0.1',0));port=sock.getsockname()[1];sock.close();ready=threading.Event();events=[];b=None
 def event(m):
  events.append(m)
  if m['event']=='ready':ready.set()
  if m.get('uiId'):b.reply(m['uiId'],{'allowed':True,'copied':True,'opened':True})
 def launch():
  global b
  ready.clear();b=Backend(str(HOST),lambda f:f(),event,lambda e:print('EXIT',e),['--data-dir',tmp,'--offline-test','--port',str(port)])
  assert ready.wait(15),'host startup'
 def call(method,**kw):
  q=queue.Queue();b.call(method,kw,lambda v,e:q.put((v,e)));v,e=q.get(timeout=20);assert not e,e;return v
 ctx=ssl._create_unverified_context() # localhost fixture only; Android pins production cert
 def request(method,path,data=None,token=None,raw=False):
  headers={}
  if token:headers['Authorization']='Bearer '+token
  if data is not None and not raw:data=json.dumps(data).encode();headers['Content-Type']='application/json'
  req=urllib.request.Request('https://127.0.0.1:'+str(port)+path,data=data,headers=headers,method=method)
  try:
   with urllib.request.urlopen(req,context=ctx,timeout=10) as r:return r.status,json.load(r)
  except urllib.error.HTTPError as e:return e.code,json.load(e)
 launch();hello=request('GET','/v1/hello')[1];check(len(hello['fingerprint'])==64,'TLS receiver supplies certificate pin');check(request('GET','/v1/outbox')[0]==401,'unapproved outbox access rejected')
 udp=socket.socket(socket.AF_INET,socket.SOCK_DGRAM);udp.settimeout(3);udp.sendto(b'ACTIONBRIDGE_DISCOVER_V1',('127.0.0.1',45832));discovery=json.loads(udp.recv(4096));udp.close();check(discovery['id']==hello['id'] and discovery['port']==port,'actual UDP discovery identifies Ubuntu receiver')
 client=str(uuid.uuid4());token=os.urandom(32).hex();check(request('POST','/v1/pair',{'clientId':client,'name':'Test phone','token':token})[0]==200,'pairing requires and receives desktop approval');check(request('GET','/v1/status',token=token)[0]==200,'approved phone authenticates')
 payload=b'Ubuntu file transfer\n'*1000;job={'id':str(uuid.uuid4()),'name':'../received.txt','size':len(payload),'sha256':hashlib.sha256(payload).hexdigest(),'action':'save'}
 check(request('POST','/v1/jobs',job,token)[0]==200,'incoming file job created');check(request('PUT','/v1/jobs/'+job['id']+'/content?offset=0',payload,token,True)[1]['offset']==len(payload),'HTTPS file upload commits durable offset')
 request('POST','/v1/jobs/'+job['id']+'/finish',token=token)
 for _ in range(100):
  state=request('GET','/v1/jobs/'+job['id'],token=token)[1]['state']
  if state=='completed':break
  time.sleep(.02)
 check(state=='completed','incoming file completes');received=root/'downloads/ActionBridge'/ (job['id']+'-received.txt');check(received.read_bytes()==payload,'path is sanitized and received bytes match')
 check(request('POST','/v1/jobs/'+job['id']+'/finish',token=token)[1]['state']=='completed','finish retry is idempotent')
 for action,text in [('copy','Phone → Ubuntu क'),('url','https://example.com')]:
  j=dict(id=str(uuid.uuid4()),name='Text',size=0,sha256='',action=action,text=text);request('POST','/v1/jobs',j,token);request('POST','/v1/jobs/'+j['id']+'/finish',token=token)
  for _ in range(100):
   state=request('GET','/v1/jobs/'+j['id'],token=token)[1]['state']
   if state in ('completed','failed'):break
   time.sleep(.02)
  check(state=='completed',action+' action traverses private desktop IPC')
 check(len(call('snapshot')['jobs'])==3,'activity includes received files and actions')
 source=root/'send.txt';source.write_bytes(payload);check(call('sendFiles',recipient=client,paths=[str(source)])['queued']==1,'GUI stages outgoing file');source.write_text('changed original');item=request('GET','/v1/outbox',token=token)[1][0]
 data=request('GET','/v1/outbox/'+item['id']+'?offset=0&count=262144',token=token)[1];check(base64.b64decode(data['data'])==payload,'outgoing content is a private immutable snapshot');check(request('POST','/v1/outbox/'+item['id']+'/ack',token=token)[1]['delivered'],'phone acknowledges outgoing file');check(not (root/'outbox'/(item['id']+'.payload')).exists(),'acknowledged staged body removed')
 check(request('POST','/v1/outbox/'+item['id']+'/ack',token=token)[0]==200,'outgoing acknowledgement retry safe');call('sendText',recipient=client,text='Hello phone',kind='text');check(request('GET','/v1/outbox',token=token)[1][0]['text']=='Hello phone','PC text appears in phone outbox')
 check(root.stat().st_mode&0o777==0o700 and (root/'identity.pfx').stat().st_mode&0o777==0o600,'pairing data and identity are private');q=queue.Queue();duplicate=Backend(str(HOST),lambda f:f(),lambda m:q.put(m),lambda e:None,['--data-dir',tmp,'--offline-test','--no-discovery','--port',str(port+1)]);fatal=q.get(timeout=10);check(fatal['event']=='fatal' and 'already running' in fatal['data']['message'],'second host cannot take same user identity');duplicate.stop()
 b.stop();launch();after=request('GET','/v1/hello')[1];check(after['id']==hello['id'] and after['fingerprint']==hello['fingerprint'],'computer ID and certificate survive restart');check(request('GET','/v1/status',token=token)[0]==200,'approved phone persists across restart');check(len(call('snapshot')['jobs'])==3,'activity survives receiver restart');call('revoke',id=client);check(request('GET','/v1/status',token=token)[0]==401,'removed phone loses access');b.stop()
print(count,'Ubuntu integration checks passed.')
