using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
namespace ActionBridge.Core;
public sealed class TrustStore {
    readonly string path; readonly object gate = new();
    readonly Dictionary<string, Phone> phones;
    public TrustStore(string folder) {
        Directory.CreateDirectory(folder); path = Path.Combine(folder, "phones.json");
        phones = File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, Phone>>(File.ReadAllText(path), Wire.Json) ?? new() : new();
    }
    public event Action? Changed;
    void Notify(){try{Changed?.Invoke();}catch{/* UI observers cannot change the approval result. */}}
    public Phone[] List() { lock(gate) return phones.Values.ToArray(); }
    public string? Authenticate(string token) {
        if (token.Length != 64 || !token.All(Uri.IsHexDigit)) return null;
        var hash = Encoding.ASCII.GetBytes(Wire.Hash(token));
        lock(gate) return phones.Values.FirstOrDefault(p => CryptographicOperations.FixedTimeEquals(hash, Encoding.ASCII.GetBytes(p.TokenHash)))?.ClientId;
    }
    public bool AlreadyApproved(PairRequest r) => Authenticate(r.Token) == r.ClientId;
    public void Approve(PairRequest r) {
        if (!Guid.TryParse(r.ClientId, out _) || string.IsNullOrEmpty(r.Name) || string.IsNullOrEmpty(r.Token) || r.Token.Length != 64 || !r.Token.All(Uri.IsHexDigit) || r.Name.Length is < 1 or > 80) throw new ArgumentException("Invalid phone identity.");
        lock(gate) {
            if (phones.Count >= 32 && !phones.ContainsKey(r.ClientId)) throw new InvalidOperationException("Device limit reached. Remove an old phone.");
            var next=new Dictionary<string,Phone>(phones){[r.ClientId]=new(r.ClientId,r.Name,Wire.Hash(r.Token))};Wire.AtomicJson(path,next);phones[r.ClientId]=next[r.ClientId];
        }
        Notify();
    }
    public void Revoke(string id) { lock(gate) { var next=new Dictionary<string,Phone>(phones);next.Remove(id);Wire.AtomicJson(path,next);phones.Remove(id); } Notify(); }
}
