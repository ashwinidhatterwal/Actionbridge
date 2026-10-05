using System.Security.Cryptography;
using System.Text.Json;
namespace ActionBridge.Core;
public sealed record Outgoing(string Id,string Recipient,string Name,string Kind,long Size,string Sha256,string? Text,string State,DateTime Updated,long Offset=0);
/** PC-owned staged copies: phones can read only items explicitly addressed to them. */
public sealed class Outbox {
 readonly string root;readonly object gate=new();readonly Dictionary<string,Outgoing> items=new();
 public event Action? Changed;
 public Outbox(string root){this.root=root;Directory.CreateDirectory(root);foreach(var p in Directory.GetFiles(root,"*.json")){try{var item=JsonSerializer.Deserialize<Outgoing>(File.ReadAllText(p),Wire.Json)!;items[item.Id]=item;}catch{}}foreach(var payload in Directory.GetFiles(root,"*.payload")){var id=Path.GetFileNameWithoutExtension(payload);if(Guid.TryParseExact(id,"D",out _)&&!items.ContainsKey(id)&&!File.Exists(Path.Combine(root,id+".json")))File.Delete(payload);}}
 void Notify(){try{Changed?.Invoke();}catch{/* UI observers cannot interrupt durable delivery. */}}
 void Save(Outgoing item){Wire.AtomicJson(Path.Combine(root,item.Id+".json"),item);items[item.Id]=item;Notify();}
 public Outgoing[] ListAll(string recipient){lock(gate)return items.Values.Where(x=>x.Recipient==recipient).ToArray();}
 public Outgoing[] History(){lock(gate)return items.Values.OrderByDescending(x=>x.Updated).Take(100).ToArray();}
 public Outgoing[] List(string recipient){lock(gate)return items.Values.Where(x=>x.Recipient==recipient&&x.State=="queued").OrderBy(x=>x.Updated).Take(1).ToArray();}
 public async Task<Outgoing> Stage(string recipient,string? file,string? text=null,string kind="file"){
  if(string.IsNullOrWhiteSpace(recipient))throw new ArgumentException("Choose a paired phone.");
  lock(gate){if(items.Values.Count(x=>x.State=="queued")>=100)throw new InvalidOperationException("Outbox is full. Cancel waiting files first.");}
  var id=Guid.NewGuid().ToString();long size=0;var hash="";var name="Text";
  if(kind=="file"){
   if(file==null)throw new ArgumentException("Choose a file.");name=Wire.SafeName(Path.GetFileName(file));var staged=Path.Combine(root,id+".payload");
   try{await using var input=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read,262144,true);if(input.Length>Wire.MaxFile)throw new ArgumentException("Files must be at most 2 GB.");await using(var output=new FileStream(staged,FileMode.CreateNew,FileAccess.Write,FileShare.None,262144,true)){await input.CopyToAsync(output);output.Flush(true);size=output.Length;}await using var read=File.OpenRead(staged);hash=Convert.ToHexString(await SHA256.HashDataAsync(read)).ToLowerInvariant();}catch{File.Delete(staged);throw;}
  }else{
   if(kind is not ("text" or "link")||string.IsNullOrEmpty(text)||System.Text.Encoding.UTF8.GetByteCount(text)>8192)throw new ArgumentException("Text must be at most 8 KB.");
   if(kind=="link"&&(!Uri.TryCreate(text,UriKind.Absolute,out var u)||u.Scheme is not ("http" or "https")||u.UserInfo!=""))throw new ArgumentException("Enter an HTTP or HTTPS link.");name=kind=="link"?"Web link":"Text";
  }
  var item=new Outgoing(id,recipient,name,kind,size,hash,text,"queued",DateTime.UtcNow);lock(gate)Save(item);return item;
 }
 Outgoing Get(string id,string recipient){if(!Guid.TryParseExact(id,"D",out var uuid)||id!=uuid.ToString())throw new ArgumentException("Invalid transfer ID.");if(!items.TryGetValue(id,out var item)||item.Recipient!=recipient)throw new KeyNotFoundException("Transfer unavailable.");return item;}
 public object Read(string id,string recipient,long offset,int count){lock(gate){var item=Get(id,recipient);if(item.Kind!="file"||item.State!="queued"||count is <1 or >262144||offset<0||offset>item.Size)throw new ArgumentException("Invalid download request.");using var input=File.OpenRead(Path.Combine(root,id+".payload"));input.Position=offset;var bytes=new byte[(int)Math.Min(count,item.Size-offset)];input.ReadExactly(bytes);items[id]=item with{Offset=Math.Max(item.Offset,offset+bytes.Length)};if(offset+bytes.Length-item.Offset>=16384)Notify();return new{offset,data=Convert.ToBase64String(bytes)};}}
 public object Acknowledge(string id,string recipient){lock(gate){var item=Get(id,recipient);if(item.State=="cancelled")throw new InvalidOperationException("Transfer cancelled.");if(item.State!="delivered"){Save(item with{State="delivered",Offset=item.Size,Updated=DateTime.UtcNow});File.Delete(Path.Combine(root,id+".payload"));}return new{delivered=true};}}
 public void Cancel(string id){lock(gate){if(items.TryGetValue(id,out var item)&&item.State=="queued"){Save(item with{State="cancelled",Updated=DateTime.UtcNow});File.Delete(Path.Combine(root,id+".payload"));}}}
}
