"""Asynchronous, bounded private-pipe client. No credentials are passed in argv."""
import json,subprocess,threading,uuid
class Backend:
 def __init__(self,path,dispatch,on_event,on_exit,arguments=()):
  self.dispatch=dispatch;self.on_event=on_event;self.on_exit=on_exit;self.pending={};self.lock=threading.RLock();self.stopping=False
  self.process=subprocess.Popen([path,*arguments],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.DEVNULL,text=True,encoding='utf-8',bufsize=1,close_fds=True)
  threading.Thread(target=self._read,daemon=True).start()
 def _write(self,message):
  with self.lock:
   if self.process.poll() is not None:raise RuntimeError('Receiver has stopped. Restart it from Preferences.')
   self.process.stdin.write(json.dumps(message,ensure_ascii=False,separators=(',',':'))+'\n');self.process.stdin.flush()
 def call(self,method,values=None,callback=None,timeout=60):
  request=uuid.uuid4().hex
  def expired():
   with self.lock:entry=self.pending.pop(request,None)
   if entry and callback:self.dispatch(lambda:callback(None,'Request timed out. Check activity before retrying.'))
  timer=threading.Timer(timeout,expired);timer.daemon=True
  with self.lock:self.pending[request]=(callback,timer)
  try:self._write(dict(values or {},method=method,requestId=request));timer.start()
  except Exception as e:
   with self.lock:self.pending.pop(request,None)
   if callback:self.dispatch(lambda message=str(e):callback(None,message))
  return request
 def reply(self,id,result=None,error=None):
  try:self._write(dict(uiReply=id,**({'error':error} if error else {'result':result or {}})))
  except (OSError,RuntimeError):pass
 def _read(self):
  reason='Receiver stopped. Restart it from Preferences.'
  try:
   while True:
    line=self.process.stdout.readline(4*1024*1024+1)
    if not line:break
    if len(line)>4*1024*1024:raise ValueError('Invalid receiver response')
    m=json.loads(line)
    if 'requestId' in m:
     with self.lock:entry=self.pending.pop(m['requestId'],None)
     if entry:
      callback,timer=entry;timer.cancel()
      if callback:self.dispatch(lambda cb=callback,value=m.get('result'),error=m.get('error'):cb(value,error))
    elif 'event' in m:self.dispatch(lambda value=m:self.on_event(value))
  except Exception:reason='Receiver connection interrupted. Restart it from Preferences.'
  with self.lock:entries=list(self.pending.values());self.pending.clear()
  for callback,timer in entries:
   timer.cancel()
   if callback:self.dispatch(lambda cb=callback:cb(None,reason))
  if not self.stopping:self.dispatch(lambda:self.on_exit(reason))
 def stop(self):
  self.stopping=True
  try:self._write({'method':'quit'});self.process.stdin.close();self.process.wait(timeout=5)
  except Exception:
   try:self.process.terminate();self.process.wait(timeout=2)
   except Exception:
    try:self.process.kill()
    except Exception:pass
