const appOrigin='https://appassets.androidplatform.net';
const keyPattern=/^[A-Za-z0-9_-]{43}$/;
const roomPattern=/^[a-f0-9]{32}$/;
export const randomKey=()=>{const b=crypto.getRandomValues(new Uint8Array(32));return btoa(String.fromCharCode(...b)).replaceAll('+','-').replaceAll('/','_').replaceAll('=','');};
export async function digest(s){return Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',new TextEncoder().encode(s))),b=>b.toString(16).padStart(2,'0')).join('');}
export function equal(a,b){if(typeof a!=='string'||typeof b!=='string'||a.length!==b.length)return false;let x=0;for(let i=0;i<a.length;i++)x|=a.charCodeAt(i)^b.charCodeAt(i);return x===0;}
const response=(value,status=200)=>new Response(JSON.stringify(value),{status,headers:{'Content-Type':'application/json','Cache-Control':'no-store'}});
export function validSignal(m){return m&&['offer','answer','candidate','cancel'].includes(m.type)&&/^[a-f0-9]{32}$/.test(m.session)&&typeof m.sdp==='string'&&m.sdp.length<=48000&&/^[a-f0-9]{64}$/.test(m.mac);}
export default {
 async fetch(request,env){
  const u=new URL(request.url),origin=request.headers.get('Origin');
  if(origin&&origin!==appOrigin&&origin!==u.origin)return response({error:'Origin refused'},403);
  if(request.method==='OPTIONS')return new Response(null,{headers:{'Access-Control-Allow-Origin':origin||appOrigin,'Access-Control-Allow-Headers':'Authorization,Content-Type','Access-Control-Allow-Methods':'GET,POST,DELETE,OPTIONS','Vary':'Origin'}});
  let r;
  try{
   if(u.pathname==='/health')r=response({service:'ActionBridge connection service',version:2,automaticEnrollment:env.AUTO_ENROLLMENT_ENABLED==='true',relay:env.RELAY_ENABLED==='true'});
   else if(u.pathname==='/enroll'&&request.method==='POST'){
    if(env.AUTO_ENROLLMENT_ENABLED!=='true')r=response({error:'Automatic setup is disabled'},403);
    else if(!env.ENROLL_IP_LIMIT||!env.ENROLL_GLOBAL_LIMIT)r=response({error:'Enrollment limits are not configured'},503);
    else if(!(await env.ENROLL_GLOBAL_LIMIT.limit({key:'enroll'})).success||!(await env.ENROLL_IP_LIMIT.limit({key:request.headers.get('CF-Connecting-IP')||'unknown'})).success)r=response({error:'Please wait a minute before setting up another PC'},429);
    else if(Number(request.headers.get('Content-Length'))>2048)r=response({error:'Request too large'},413);
    else{
     const raw=await request.text();if(raw.length>2048)return response({error:'Request too large'},413);
     let b;try{b=JSON.parse(raw)}catch{return response({error:'Invalid enrollment'},400)};
     if(!keyPattern.test(b.pcKey||'')||!keyPattern.test(b.phoneKey||'')||b.pcKey===b.phoneKey)r=response({error:'Invalid installation identity'},400);
     else{
      const pc=await digest(b.pcKey),phone=await digest(b.phoneKey),id=pc.slice(0,32);
      const result=await env.ROOMS.get(env.ROOMS.idFromName(id)).fetch(new Request('https://internal/init',{method:'POST',body:JSON.stringify({pc,phone,automatic:true})}));
      r=result.ok?response({room:id}):response({error:'Installation identity conflict'},409);
     }
    }
   }
   else if(u.pathname==='/rooms'&&request.method==='POST'){
    if(env.REGISTRATION_ENABLED!=='true'||!env.ENROLLMENT_KEY||!equal(request.headers.get('Authorization'),'Bearer '+env.ENROLLMENT_KEY))r=response({error:'Registration disabled or setup key incorrect'},403);
    else{
     const id=crypto.randomUUID().replaceAll('-',''),pcKey=randomKey(),phoneKey=randomKey();
     const stub=env.ROOMS.get(env.ROOMS.idFromName(id));
     await stub.fetch(new Request('https://internal/init',{method:'POST',body:JSON.stringify({pc:await digest(pcKey),phone:await digest(phoneKey)})}));
     r=response({room:id,pcKey,phoneKey});
    }
   }else{
    const match=u.pathname.match(/^\/rooms\/([a-f0-9]{32})\/(ws|ice|delete)$/);
    if(!match)r=response({error:'Not found'},404);
    else r=await env.ROOMS.get(env.ROOMS.idFromName(match[1])).fetch(request);
   }
  }catch{r=response({error:'Connection service unavailable'},503);}
  if(r.status===101)return r;
  const headers=new Headers(r.headers);headers.set('Access-Control-Allow-Origin',origin||appOrigin);headers.set('Vary','Origin');headers.set('Cache-Control','no-store');headers.set('X-Content-Type-Options','nosniff');
  return new Response(r.body,{status:r.status,headers});
 }
};
export class ConnectionRoom {
 constructor(ctx,env){this.ctx=ctx;this.env=env;ctx.setWebSocketAutoResponse(new WebSocketRequestResponsePair('ping','pong'));}
 async fetch(request){
  const u=new URL(request.url);
  if(u.hostname==='internal'&&u.pathname==='/init'){
   const keys=await request.json(),old=await this.ctx.storage.get('keys');
   if(old)return equal(old.pc,keys.pc)&&equal(old.phone,keys.phone)?response({ok:true}):response({error:'Exists'},409);
   await this.ctx.storage.put('keys',{pc:keys.pc,phone:keys.phone});if(keys.automatic)await this.ctx.storage.setAlarm(Date.now()+48*3600000);return response({ok:true});
  }
  const keys=await this.ctx.storage.get('keys');if(!keys)return response({error:'PC removed or unknown'},404);
  const role=u.searchParams.get('role');if(!['pc','phone'].includes(role))return response({error:'Invalid role'},400);
  // WebSocket credentials use subprotocols, never query strings or access logs.
  const protocols=(request.headers.get('Sec-WebSocket-Protocol')||'').split(',').map(s=>s.trim());
  const token=u.pathname.endsWith('/ws')?protocols.find(p=>p.startsWith('key.'))?.slice(4):request.headers.get('Authorization')?.replace(/^Bearer /,'');
  if(!keyPattern.test(token||'')||!equal(await digest(token),keys[role]))return response({error:'Device access revoked'},401);
  if(u.pathname.endsWith('/delete')&&request.method==='DELETE'){
   if(role!=='pc')return response({error:'PC permission required'},403);
   for(const s of this.ctx.getWebSockets())s.close(1008,'Access revoked');await this.ctx.storage.deleteAll();return response({ok:true});
  }
  if(u.pathname.endsWith('/ice')&&request.method==='GET'){
   const servers=[{urls:['stun:stun.cloudflare.com:3478']}];
   if(this.env.RELAY_ENABLED==='true'){
    if(!this.env.TURN_KEY_ID||!this.env.TURN_API_TOKEN)return response({error:'Relay configuration incomplete'},503);
    // Persist a per-role throttle; no heartbeat or transfer-byte writes.
    const last=await this.ctx.storage.get('ice:'+role)||0;
    if(Date.now()-last<5000)return response({error:'Please wait before reconnecting'},429);
    await this.ctx.storage.put('ice:'+role,Date.now());
    const result=await fetch('https://rtc.live.cloudflare.com/v1/turn/keys/'+encodeURIComponent(this.env.TURN_KEY_ID)+'/credentials/generate-ice-servers',{method:'POST',headers:{Authorization:'Bearer '+this.env.TURN_API_TOKEN,'Content-Type':'application/json'},body:JSON.stringify({ttl:3600})});
    if(!result.ok)return response({error:'Relay temporarily unavailable'},503);
    const value=await result.json();servers.push(...value.iceServers.filter(s=>s.urls?.some?.(x=>x.startsWith('turn'))));
   }
   return response({iceServers:servers,relay:this.env.RELAY_ENABLED==='true',maxFile:2147483648});
  }
  if(!u.pathname.endsWith('/ws')||request.headers.get('Upgrade')?.toLowerCase()!=='websocket'||!protocols.includes('ab1'))return response({error:'WebSocket required'},400);
  if(role==='phone'){await this.ctx.storage.put('activated',true);await this.ctx.storage.deleteAlarm();}
  for(const s of this.ctx.getWebSockets(role))s.close(1000,'New connection');
  const [client,server]=Object.values(new WebSocketPair());this.ctx.acceptWebSocket(server,[role]);server.serializeAttachment({role,count:0,window:Date.now()});
  const other=role==='pc'?'phone':'pc';
  server.send(JSON.stringify({type:'presence',online:this.ctx.getWebSockets(other).some(s=>s.readyState===1)}));
  for(const s of this.ctx.getWebSockets(other))try{s.send(JSON.stringify({type:'presence',online:true}));}catch{}
  return new Response(null,{status:101,webSocket:client,headers:{'Sec-WebSocket-Protocol':'ab1'}});
 }
 async alarm(){if(this.ctx.getWebSockets().some(s=>s.readyState===1)){await this.ctx.storage.setAlarm(Date.now()+48*3600000);return;}if(!await this.ctx.storage.get('activated')){for(const s of this.ctx.getWebSockets())s.close(1008,'Unused enrollment expired');await this.ctx.storage.deleteAll();}}
 async webSocketMessage(ws,message){
  if(typeof message!=='string'||message.length>50000){ws.close(1009,'Only connection messages allowed');return;}
  const a=ws.deserializeAttachment();if(Date.now()-a.window>60000){a.count=0;a.window=Date.now();}if(++a.count>200){ws.close(1008,'Connection message limit');return;}ws.serializeAttachment(a);
  let m;try{m=JSON.parse(message);}catch{ws.close(1008,'Invalid message');return;}
  if(!validSignal(m)){ws.close(1008,'Invalid signal');return;}
  if((a.role==='pc'&&m.type==='offer')||(a.role==='phone'&&m.type==='answer')){ws.close(1008,'Invalid direction');return;}
  for(const s of this.ctx.getWebSockets(a.role==='pc'?'phone':'pc'))try{s.send(message);}catch{}
 }
 async webSocketClose(ws,code){ws.close(code);const role=ws.deserializeAttachment()?.role;for(const s of this.ctx.getWebSockets(role==='pc'?'phone':'pc'))try{s.send(JSON.stringify({type:'presence',online:this.ctx.getWebSockets(role).some(s=>s!==ws&&s.readyState===1)}));}catch{}}
 async webSocketError(ws){ws.close(1011,'Reconnect');}
}
