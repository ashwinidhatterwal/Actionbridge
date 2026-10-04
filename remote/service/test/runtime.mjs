// Local integration test; the sandbox denies interface enumeration.
import os from 'node:os';
os.networkInterfaces=()=>({lo:[{address:'127.0.0.1',family:'IPv4',netmask:'255.0.0.0',internal:true,cidr:'127.0.0.1/8',mac:'00:00:00:00:00:00'}]});
const {Miniflare,convertV4MiniflareOptions}=await import('miniflare');
const assert=(await import('node:assert/strict')).default;
const mf=new Miniflare(convertV4MiniflareOptions({host:"127.0.0.1",name:"test",modules:true,scriptPath:new URL('../src/worker.mjs',import.meta.url).pathname,compatibilityDate:'2026-10-01',durableObjects:{ROOMS:{className:'ConnectionRoom',useSQLite:true}},ratelimits:{ENROLL_IP_LIMIT:{namespace_id:'458331',simple:{limit:3,period:60}},ENROLL_GLOBAL_LIMIT:{namespace_id:'458332',simple:{limit:60,period:60}}},bindings:{AUTO_ENROLLMENT_ENABLED:'true',REGISTRATION_ENABLED:'true',ENROLLMENT_KEY:'test-only',RELAY_ENABLED:'false'}}));
try{
 const req=(path,options)=>mf.dispatchFetch('https://test.example'+path,options);
 const enrollKeys={pcKey:'p'.repeat(43),phoneKey:'q'.repeat(43)};
 const enroll=()=>req('/enroll',{method:'POST',headers:{'Content-Type':'application/json','CF-Connecting-IP':'192.0.2.1'},body:JSON.stringify(enrollKeys)});
 let a=await enroll();assert.equal(a.status,200);const auto=await a.json();assert.equal(Object.keys(auto).join(','),'room');
 a=await enroll();assert.equal(a.status,200);assert.equal((await a.json()).room,auto.room);
 a=await enroll();assert.equal(a.status,200);a=await enroll();assert.equal(a.status,429);
 const autoIce=await req('/rooms/'+auto.room+'/ice?role=phone',{headers:{Authorization:'Bearer '+enrollKeys.phoneKey}});assert.equal(autoIce.status,200);
 console.log('PASS: automatic enrollment is idempotent, returns no credentials, rate-limits new installs and preserves access');
 const r=await req('/rooms',{method:'POST',headers:{Authorization:'Bearer test-only'}});assert.equal(r.status,200);const room=await r.json();assert.equal(room.pcKey.length,43);
 let ice=await req('/rooms/'+room.room+'/ice?role=phone',{headers:{Authorization:'Bearer '+room.phoneKey,Origin:'https://appassets.androidplatform.net'}});assert.equal(ice.status,200);assert.equal((await ice.json()).relay,false);
 const denied=await req('/rooms/'+room.room+'/ice?role=phone',{headers:{Authorization:'Bearer '+'a'.repeat(43)}});assert.equal(denied.status,401);
 const pc=await req('/rooms/'+room.room+'/ws?role=pc',{headers:{Upgrade:'websocket','Sec-WebSocket-Protocol':'ab1,key.'+room.pcKey}});assert.equal(pc.status,101);pc.webSocket.accept();
 const phone=await req('/rooms/'+room.room+'/ws?role=phone',{headers:{Upgrade:'websocket','Sec-WebSocket-Protocol':'ab1,key.'+room.phoneKey,Origin:'https://appassets.androidplatform.net'}});assert.equal(phone.status,101);phone.webSocket.accept();
 const delivered=new Promise((resolve,reject)=>{const timer=setTimeout(()=>reject(Error('signal timeout')),4000);pc.webSocket.addEventListener('message',e=>{const m=JSON.parse(e.data);if(m.type==='offer'){clearTimeout(timer);resolve(m)}})});
 phone.webSocket.send(JSON.stringify({type:'offer',session:'a'.repeat(32),sdp:'connection-only',mac:'b'.repeat(64)}));assert.equal((await delivered).sdp,'connection-only');
 const forbidden=await req('/rooms/'+room.room+'/delete?role=phone',{method:'DELETE',headers:{Authorization:'Bearer '+room.phoneKey}});assert.equal(forbidden.status,403);
 const removed=await req('/rooms/'+room.room+'/delete?role=pc',{method:'DELETE',headers:{Authorization:'Bearer '+room.pcKey}});assert.equal(removed.status,200);
 const revoked=await req('/rooms/'+room.room+'/ice?role=phone',{headers:{Authorization:'Bearer '+room.phoneKey}});assert.equal(revoked.status,404);
 console.log('PASS: runtime room creation, auth, WebSocket signaling, phone permission boundary and revocation');
}finally{await mf.dispose()}
