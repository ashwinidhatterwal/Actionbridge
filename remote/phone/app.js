const $=id=>document.getElementById(id),button=$('send');
const hex=b=>Array.from(b,x=>x.toString(16).padStart(2,'0')).join('');
let c,ws,pc,dc,sid,online=false,busy=false,ack=0,waiters=[],pending=new Map(),heartbeat,local=[],remote=[],offered=false,chain=Promise.resolve(),epoch=0,negotiating=false,startedAt=0;
function text(s){$('status').textContent=s;window.NativeBridge?.status(s)}
function decode(s){return new TextDecoder().decode(Uint8Array.from(atob(s.replaceAll('-','+').replaceAll('_','/')),x=>x.charCodeAt(0)))}
function rejectAll(e){for(const p of [...pending.values(),...waiters]){clearTimeout(p.timer);p.reject(e)}pending.clear();waiters=[]}
function drop(){epoch++;negotiating=false;startedAt=0;window.NativeBridge?.ready(false);clearInterval(heartbeat);
 if(ws){const socket=ws;ws=null;socket.onclose=null;socket.close()}
 const old=pc;pc=null;dc=null;if(old)old.close();online=false;rejectAll(new Error('Connection interrupted. Reconnect to resume.'));button.disabled=true}
function wait(type,timeout=60000){return new Promise((resolve,reject)=>{const timer=setTimeout(()=>{pending.delete(type);reject(new Error('PC response timed out'))},timeout);pending.set(type,{resolve,reject,timer})})}
async function mac(type,sdp,id=sid,secret=c.secret){const key=await crypto.subtle.importKey('raw',new TextEncoder().encode(secret),{name:'HMAC',hash:'SHA-256'},false,['sign']);return hex(new Uint8Array(await crypto.subtle.sign('HMAC',key,new TextEncoder().encode(id+'\n'+type+'\n'+sdp))))}
function signal(type,sdp){const id=sid,generation=epoch,socket=ws,secret=c.secret;chain=chain.then(async()=>{
 if(generation!==epoch||socket!==ws||socket?.readyState!==1)return;const signature=await mac(type,sdp,id,secret);
 if(generation===epoch&&socket===ws&&socket.readyState===1)socket.send(JSON.stringify({type,session:id,sdp,mac:signature}));}).catch(()=>{});return chain}
async function negotiate(){if(pc||!online||negotiating)return;const generation=epoch,config=c;negotiating=true;startedAt=Date.now();
 try{text('Connecting to your PC…');sid=hex(crypto.getRandomValues(new Uint8Array(16)));offered=false;local=[];remote=[];
 const r=await fetch(config.service+'/rooms/'+config.room+'/ice?role=phone',{headers:{Authorization:'Bearer '+config.key},cache:'no-store'}),ice=await r.json();
 if(generation!==epoch||config!==c)return;if(!r.ok)throw new Error(ice.error||'Service unavailable');
 pc=new RTCPeerConnection({iceServers:ice.iceServers});const peer=pc;
 pc.onicecandidate=e=>{if(pc!==peer)return;if(e.candidate){const candidate=JSON.stringify(e.candidate.toJSON());if(offered)signal('candidate',candidate);else local.push(candidate)}};
 pc.onconnectionstatechange=()=>{if(pc!==peer)return;if(pc.connectionState==='failed'){drop();text('Connection failed. Reconnecting…')}else if(pc.connectionState==='connected')route()};
 dc=pc.createDataChannel(window.NativeBridge?'jobs-v2':'files-v1',{ordered:true});const channel=dc;dc.binaryType='arraybuffer';
 dc.onopen=()=>{if(dc!==channel||pc!==peer)return;button.disabled=busy;window.NativeBridge?.ready(true);route()};
 dc.onclose=()=>{if(dc!==channel||pc!==peer)return;drop();text('Connection interrupted. Reconnecting; retry the transfer to resume.')};
 dc.onmessage=e=>{if(dc!==channel||pc!==peer||typeof e.data!=='string')return;const m=JSON.parse(e.data);if(m.type==='rpc'){const p=pending.get(m.requestId);if(p){clearTimeout(p.timer);pending.delete(m.requestId);if(m.error)p.reject(new Error(m.error));else p.resolve(m.result)}return}if(m.type==='error'){rejectAll(new Error(m.error));text(m.error);return}if(m.type==='ack'){ack=m.offset;const rest=[];for(const p of waiters){if(ack>=p.target){clearTimeout(p.timer);p.resolve()}else rest.push(p)}waiters=rest;return}const type=m.type==='saved'&&pending.has('ready')?'ready':m.type,p=pending.get(type);if(p){clearTimeout(p.timer);pending.delete(type);p.resolve(m)}};
 const offer=await pc.createOffer();if(generation!==epoch||pc!==peer)return;await peer.setLocalDescription(offer);if(generation!==epoch||pc!==peer)return;
 await signal('offer',offer.sdp);if(generation!==epoch||pc!==peer)return;offered=true;for(const candidate of local)await signal('candidate',candidate);local=[];
 }finally{if(generation===epoch)negotiating=false}
}
async function route(){const peer=pc;if(!peer)return;let label='Connected';try{const stats=await peer.getStats();for(const s of stats.values())if(s.type==='transport'&&s.selectedCandidatePairId){const pair=stats.get(s.selectedCandidatePairId),a=stats.get(pair?.localCandidateId),b=stats.get(pair?.remoteCandidateId);label=a?.candidateType==='relay'||b?.candidateType==='relay'?'Connected · encrypted relay':'Connected · direct transfer'}}catch{}if(pc===peer)text(label)}
function connect(){drop();try{if(!c)throw new Error('Scan your PC pairing QR code.');startedAt=Date.now();text('Finding your PC…');const generation=epoch,config=c;
 ws=new WebSocket(c.service.replace(/^https:/,'wss:')+'/rooms/'+c.room+'/ws?role=phone',['ab1','key.'+c.key]);const socket=ws;
 ws.onopen=()=>{if(socket!==ws)return;heartbeat=setInterval(()=>{if(socket===ws&&socket.readyState===1)socket.send('ping')},45000)};
 let incoming=Promise.resolve();ws.onmessage=e=>{if(socket!==ws||e.data==='pong')return;incoming=incoming.then(async()=>{
 if(socket!==ws||generation!==epoch)return;const m=JSON.parse(e.data);
 if(m.type==='presence'){online=m.online;if(online)await negotiate();else {const peer=pc;pc=null;dc=null;if(peer)peer.close();window.NativeBridge?.ready(false);rejectAll(new Error('PC disconnected. Retry when it is awake.'));text('PC is offline. It stays saved; reconnect when it is awake.');}return}
 if(m.session!==sid||m.mac!==await mac(m.type,m.sdp,m.session,config.secret)||socket!==ws||generation!==epoch||!pc)return;const peer=pc;
 if(m.type==='answer'){await peer.setRemoteDescription({type:'answer',sdp:m.sdp});if(pc!==peer)return;for(const x of remote)await peer.addIceCandidate(x);remote=[]}
 else if(m.type==='candidate'){const x=JSON.parse(m.sdp);if(peer.remoteDescription)await peer.addIceCandidate(x);else remote.push(x)}
 }).catch(e=>{if(socket===ws)text(e.message)})};
 ws.onclose=()=>{if(socket!==ws)return;drop();text('Service disconnected. Reconnecting…')};ws.onerror=()=>{if(socket===ws)text('Cannot reach service. Check your internet.')};
 }catch(e){text(e.message)}}
function ensureConnected(){if(!c||busy)return;if(dc?.readyState==='open'){window.NativeBridge?.ready(true);return}
 if(!ws||ws.readyState===3||((ws.readyState===0||online)&&Date.now()-startedAt>35000)|| (online&&!negotiating&&(!pc||pc.connectionState==='failed'||pc.connectionState==='closed'||dc?.readyState==='closed')))connect()}
function waitAck(target){if(ack>=target)return Promise.resolve();return new Promise((resolve,reject)=>{const p={target,resolve,reject};p.timer=setTimeout(()=>{waiters=waiters.filter(x=>x!==p);reject(new Error('Transfer stalled. Reconnect to resume.'))},45000);waiters.push(p)})}
window.addEventListener('beforeunload',drop);
try{c=JSON.parse(decode(location.hash.slice(1)));const u=new URL(c.service);if(u.protocol!=='https:'||u.username||u.password||u.search||u.hash||u.pathname!=='/'||!/^[a-f0-9]{32}$/.test(c.room)||!/^[A-Za-z0-9_-]{43}$/.test(c.key)||!/^[A-Za-z0-9_-]{43}$/.test(c.secret))throw new Error('Invalid pairing');c.service=u.origin;$('name').textContent=c.name||'Your PC';connect()}catch{text('Scan the remote pairing QR code in ActionBridge.')}

let retryTimer;const cancelled=new Set();
function rpc(method,values={}){if(dc?.readyState!=='open')return Promise.reject(new Error('Your PC is not connected yet.'));const requestId=crypto.randomUUID(),p=wait(requestId,180000);try{const message=JSON.stringify({type:'rpc',requestId,method,...values}),encoded=new TextEncoder().encode(message);if(encoded.length<=32000)dc.send(message);else{const total=Math.ceil(encoded.length/24000);for(let index=0;index<total;index++){const data=btoa(String.fromCharCode(...encoded.subarray(index*24000,(index+1)*24000)));dc.send(JSON.stringify({type:'fragment',requestId,index,total,data}))}}}catch(e){const pendingCall=pending.get(requestId);if(pendingCall){clearTimeout(pendingCall.timer);pending.delete(requestId);pendingCall.reject(e)}}return p}
function nativeState(id,state,message,progress=0){window.NativeBridge?.job(id,state,message,Math.max(0,Math.min(100,Math.round(progress))))}
async function sendJob(job){
 const id=job.id;cancelled.delete(id);let j=await rpc('create',{job});
 if(j.state==='uploading'&&job.action!=='copy'&&job.action!=='url'){
  const ready=wait('ready');dc.send(JSON.stringify({type:'upload',jobId:id}));const m=await ready;let offset=m.offset;ack=offset;
  if(!Number.isSafeInteger(offset)||offset<0||offset>job.size)throw new Error('Invalid resume offset');
  nativeState(id,'sending','Sending to your PC',100*offset/Math.max(job.size,1));
  while(offset<job.size){if(cancelled.has(id))throw new Error('Cancelled on this phone. Check PC activity for any action already started.');if(dc?.readyState!=='open')throw new Error('Connection interrupted. Retry this transfer to resume.');
   if(offset-ack>=524288||dc.bufferedAmount>1048576)await waitAck(offset);
   const encoded=window.NativeBridge.read(id,offset,Math.min(32768,job.size-offset));
   if(!encoded)throw new Error('Could not read the staged file.');
   const chunk=Uint8Array.from(atob(encoded),x=>x.charCodeAt(0));dc.send(chunk.buffer);offset+=chunk.length;
   nativeState(id,'sending','Sending to your PC',100*ack/Math.max(job.size,1));await new Promise(r=>setTimeout(r,0));
  }await waitAck(offset);
 }
 if(j.state==='uploading'||j.state==='queued')j=await rpc('finish',{jobId:id});
 while(j.state==='queued'||j.state==='running'){nativeState(id,'running','Your PC is completing the action',100);await new Promise(r=>setTimeout(r,800));j=await rpc('status',{jobId:id})}
 nativeState(id,j.state,j.message||'Action completed',100);
 if(j.state==='completed'||j.state==='submitted')text(job.action==='save'?'Saved on PC':j.message||'Action completed');else text(j.message||j.state);
}
window.ActionBridge={
 connect(code){if(!retryTimer)retryTimer=setInterval(()=>{ensureConnected()},8000);drop();c=JSON.parse(decode(code));c.service=new URL(c.service).origin;connect()},
 async printers(){try{const data=await rpc('printers');window.NativeBridge.printers(JSON.stringify(data))}catch(e){text(e.message)}},
 async send(jobs){if(busy||receiving){text("Finishing a received file. Try Send again in a moment.");window.NativeBridge?.busy(false);return}busy=true;window.NativeBridge?.busy(true);for(let i=0;i<jobs.length;i++){const job=jobs[i];try{await sendJob(job)}catch(e){nativeState(job.id,cancelled.has(job.id)?'cancelled':'paused',e.message);for(const other of jobs.slice(i+1))nativeState(other.id,'paused','Waiting. Retry when your PC is connected.');text(e.message);break}}busy=false;window.NativeBridge?.busy(false)},
 async cancel(id){cancelled.add(id);try{const j=await rpc('cancel',{jobId:id});nativeState(id,j.state,j.message||'Cancelled',100)}catch(e){text(e.message)}},
 ensureConnected,reconnect(){connect()},dispose(){clearInterval(retryTimer);retryTimer=null;drop()}
};
if(window.NativeBridge)retryTimer=setInterval(()=>{ensureConnected()},8000);

let receiving=false,nextReceiveAt=0;
async function receiveFromPC(){
 if(!window.NativeBridge||busy||receiving||Date.now()<nextReceiveAt||dc?.readyState!=='open')return;
 receiving=true;const channel=dc,connection=c;
 try{
  const items=await rpc('outbox');
  for(const item of items){
   if(dc!==channel||c!==connection||busy)break;
   const start=window.NativeBridge.incomingBegin(JSON.stringify(item),connection.id||connection.room);
   if(start==='error')throw new Error('Could not prepare received file.');
   if(start!=='done'){
    let offset=Number(start);if(!Number.isSafeInteger(offset)||offset<0||offset>item.size)throw new Error('Invalid download offset');
    while(offset<item.size){
     if(dc!==channel||c!==connection||busy)return;
     const positions=[];for(let i=0;i<16&&offset+i*16384<item.size;i++)positions.push(offset+i*16384);
     const blocks=await Promise.all(positions.map(position=>rpc('download',{itemId:item.id,offset:position,count:16384})));
     if(dc!==channel||c!==connection)return;
     for(const block of blocks){
      if(block.offset!==offset||!block.data)throw new Error('Invalid received chunk');
      const next=Number(window.NativeBridge.incomingAppend(item.id,offset,block.data));if(!Number.isSafeInteger(next)||next<=offset||next>item.size)throw new Error('Could not save received chunk');offset=next;
     }
    }
    if(!window.NativeBridge.incomingFinish(item.id))throw new Error('Could not finish received file');
   }
   await rpc('received',{itemId:item.id});
  }
 }catch(e){nextReceiveAt=Date.now()+30000;if(!/Unsupported action|Update the PC app/.test(e.message))text('Receiving paused: '+e.message)}finally{receiving=false}
}
const receiveTimer=window.NativeBridge?setInterval(receiveFromPC,3000):null;
