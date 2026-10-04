import {test} from 'node:test';import assert from 'node:assert/strict';import vm from 'node:vm';import {readFile} from 'node:fs/promises';import {createHmac,createHash,webcrypto,randomUUID} from 'node:crypto';
const source=await readFile(new URL('../../android/app/src/main/assets/remote/app.js',import.meta.url),'utf8');
async function scenario(interrupt=false){
 const data=Buffer.alloc(900000,73),states=[],jobs=new Map(),calls=new Map(),printers=[],elements=Object.fromEntries(['send','files','status','progress','detail','reconnect','cancel','name'].map(id=>[id,{disabled:false,textContent:'',value:0}]));
 const config={service:'https://service.example',room:'a'.repeat(32),key:'k'.repeat(43),secret:'s'.repeat(43)};
 let dc,ready=false,received=Buffer.alloc(0),activeJob,attempt=0;const timers=new Set();let receivePoll;let now=Date.now();class Clock extends Date{static now(){return now}};const inbound=Buffer.alloc(70000,91);let downloaded=Buffer.alloc(0),finished=0,ackAttempts=0,deliveries=0;const incoming={id:randomUUID(),name:"from-pc.txt",kind:"file",size:inbound.length,sha256:createHash("sha256").update(inbound).digest("hex")};
 const timer=(fn,ms)=>{const t=setTimeout(fn,Math.min(ms,250));timers.add(t);return t};
 const native={incomingBegin:()=>finished?'done':String(downloaded.length),incomingAppend:(id,offset,encoded)=>{assert.equal(id,incoming.id);assert.equal(offset,downloaded.length);downloaded=Buffer.concat([downloaded,Buffer.from(encoded,'base64')]);return String(downloaded.length)},incomingFinish:id=>{assert.equal(id,incoming.id);assert.deepEqual(downloaded,inbound);finished++;return true},status(){},ready:v=>ready=v,busy(){},printers:v=>printers.push(JSON.parse(v)),job:(id,state,message,progress)=>states.push({id,state,message,progress}),read:(id,offset,count)=>data.subarray(offset,offset+count).toString('base64')};
 class Channel{constructor(){this.readyState='connecting';this.bufferedAmount=0}send(value){if(typeof value==='string'){
  const m=JSON.parse(value);if(m.type==='fragment'){this.fragments??=[];assert(Buffer.byteLength(value)<65536);if(m.index===0)this.fragments=[];this.fragments.push(Buffer.from(m.data,'base64'));if(this.fragments.length===m.total)this.send(Buffer.concat(this.fragments).toString('utf8'));return}const respond=result=>queueMicrotask(()=>this.onmessage({data:JSON.stringify({type:'rpc',requestId:m.requestId,result})}));
  if(m.type==='rpc'){
   if(m.method==='outbox'){respond(deliveries?[]:[incoming]);return}if(m.method==='download'){assert.equal(m.itemId,incoming.id);assert(m.count<=16384);respond({offset:m.offset,data:inbound.subarray(m.offset,m.offset+m.count).toString('base64')});return}if(m.method==='received'){ackAttempts++;if(ackAttempts===1){queueMicrotask(()=>this.onmessage({data:JSON.stringify({type:'rpc',requestId:m.requestId,error:'ack lost'})}));return}deliveries++;respond({delivered:true});return}
   if(m.method==='printers'){respond([{name:'Office printer',supportsDuplex:true,papers:['A4']}]);return}
   if(m.method==='create'){const old=jobs.get(m.job.id);if(old)assert.deepEqual(old.request,m.job);else jobs.set(m.job.id,{state:'uploading',offset:0,request:m.job});respond(jobs.get(m.job.id));return}
   const j=jobs.get(m.jobId);if(m.method==='finish'){if(j.state==='uploading'){if(!['copy','url'].includes(j.request.action)){assert.equal(received.length,j.request.size);assert.equal(createHash('sha256').update(received).digest('hex'),j.request.sha256)}calls.set(m.jobId,(calls.get(m.jobId)||0)+1);j.state=j.request.action==='print'?'submitted':'completed';j.message='Action completed'}respond(j);return}respond(j);return
  }
  if(m.type==='upload'){activeJob=m.jobId;queueMicrotask(()=>this.onmessage({data:JSON.stringify({type:'ready',offset:jobs.get(activeJob).offset})}))}
 }else{
  assert(Buffer.from(value).length<=32768);received=Buffer.concat([received,Buffer.from(value)]);jobs.get(activeJob).offset=received.length;
  if(interrupt&&attempt===1&&received.length>=524288){this.readyState='closed';this.onclose();return}
  queueMicrotask(()=>this.onmessage({data:JSON.stringify({type:'ack',offset:received.length})}))
 }}}
 class Peer{constructor(){dc=new Channel();this.connectionState='connected'}createDataChannel(label){assert.equal(label,'jobs-v2');return dc}async createOffer(){return{type:'offer',sdp:'fingerprint'}}async setLocalDescription(){}async setRemoteDescription(){dc.readyState='open';dc.onopen()}async addIceCandidate(){}async getStats(){return new Map()}close(){dc.readyState='closed'}}
 class Socket{constructor(){attempt++;this.readyState=1;timer(()=>{this.onopen();this.onmessage({data:JSON.stringify({type:'presence',online:true})})},1)}send(value){if(value==='ping')return;const m=JSON.parse(value);if(m.type==='offer'){const a={type:'answer',session:m.session,sdp:'fingerprint'};a.mac=createHmac('sha256',config.secret).update(a.session+'\nanswer\n'+a.sdp).digest('hex');timer(()=>this.onmessage({data:JSON.stringify(a)}),1)}}close(){}}
 const window={NativeBridge:native,addEventListener(){}};
 const ctx=vm.createContext({Date:Clock,window,document:{getElementById:id=>elements[id]},crypto:webcrypto,TextEncoder,TextDecoder,Uint8Array,atob,btoa,URL,RTCPeerConnection:Peer,WebSocket:Socket,location:{hash:'#'+Buffer.from(JSON.stringify(config)).toString('base64url')},fetch:async()=>({ok:true,json:async()=>({iceServers:[]})}),setTimeout:timer,clearTimeout,setInterval:(fn,ms)=>{if(ms===3000)receivePoll=fn;return 0},clearInterval(){},queueMicrotask,performance,console});
 vm.runInContext(source,ctx);
 const until=async fn=>{for(let i=0;i<200;i++){if(fn())return;await new Promise(r=>setTimeout(r,5))}throw Error('Connection timed out')};
 const job={id:randomUUID(),name:'invoice.pdf',size:data.length,sha256:createHash('sha256').update(data).digest('hex'),action:'print',printer:'Office printer',copies:3,duplex:'long',pageFrom:2,pageTo:5,collate:false};
 try{await until(()=>ready);await window.ActionBridge.printers();assert.equal(printers[0][0].name,'Office printer');await window.ActionBridge.send([job]);
  if(interrupt){assert(states.some(s=>s.state==='paused'));window.ActionBridge.reconnect();await until(()=>ready);await window.ActionBridge.send([job])}
  assert.deepEqual(received,data);assert(states.some(s=>s.state==='submitted'));await window.ActionBridge.send([job]);assert.equal(calls.get(job.id),1);
  for(const action of ['copy','url']){const j={id:randomUUID(),name:'Text',size:0,sha256:'',action,text:action==='url'?'https://example.com':'क'.repeat(65536)};await window.ActionBridge.send([j]);assert.equal(calls.get(j.id),1)}
  await receivePoll();assert.equal(finished,1);assert.equal(deliveries,0);now+=31000;await receivePoll();assert.equal(finished,1);assert.equal(deliveries,1);
  assert.equal(jobs.get(job.id).request.duplex,'long');assert.equal(jobs.get(job.id).request.pageTo,5);
 }finally{window.ActionBridge.dispose();for(const t of timers)clearTimeout(t)}
}
test('native main page remote actions preserve print settings and execute retries once',()=>scenario());
test('native remote action resumes using the same durable job identity',()=>scenario(true));
