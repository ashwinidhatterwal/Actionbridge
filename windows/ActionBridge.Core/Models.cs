using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace ActionBridge.Core;
public record PairRequest(string ClientId, string Name, string Token);
public record Phone(string ClientId, string Name, string TokenHash);
public record PrintChoice(string Name, string[] Papers, bool SupportsColor, bool SupportsDuplex = false, bool IsDefault = false);
public record JobRequest(string Id, string Name, long Size, string Sha256, string Action,
    string? Printer = null, int Copies = 1, bool Landscape = false, string Paper = "Default", bool Color = true, string Fit = "fit", string? Text = null, string Duplex = "default", int PageFrom = 0, int PageTo = 0, bool Collate = true);
public sealed class Job {
    public required JobRequest Request { get; set; }
    public required string ClientId { get; set; }
    public long Offset { get; set; }
    public string State { get; set; } = "uploading";
    public string? Message { get; set; }
    public DateTime Updated { get; set; } = DateTime.UtcNow;
}
public interface IActionSink {
    Task ExecuteAsync(JobRequest request, string? file);
    IReadOnlyList<PrintChoice> Printers();
}
public static class Wire {
    public const int ApiPort = 45833, DiscoveryPort = 45832;
    public const string DiscoveryMagic = "ACTIONBRIDGE_DISCOVER_V1", Group = "239.255.42.99";
    public const long MaxFile = 2L * 1024 * 1024 * 1024;
    public const int ChunkSize = 4 * 1024 * 1024;
    public static readonly JsonSerializerOptions JsonCompact = new(JsonSerializerDefaults.Web);
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    public static void AtomicJson<T>(string path, T data) {
        var temporary = path + ".new";
        using (var fs = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None)) {
            JsonSerializer.Serialize(fs, data, Json); fs.Flush(true);
        }
        File.Move(temporary, path, true);
    }
    public static string SafeName(string name) {
        name = name.Replace('\\', '/').Split('/').Last().Trim(' ', '.');
        name = new string(name.Select(c => c < 32 || "<>:\"/\\|?*".Contains(c) ? '_' : c).ToArray());
        if (string.IsNullOrWhiteSpace(name)) name = "file";
        if (name.Length > 120) name = name[..100] + Path.GetExtension(name)[..Math.Min(20, Path.GetExtension(name).Length)];
        var stem = name.Split('.')[0].ToUpperInvariant();
        if (new[]{"CON","PRN","AUX","NUL","COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9","LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9"}.Contains(stem)) name = "_" + name;
        return name;
    }
    public static void Validate(JobRequest r) {
        if (r.Name == null || r.Sha256 == null || r.Action == null || r.Paper == null || r.Fit == null || r.Duplex == null) throw new ArgumentException("Missing transfer fields.");
        if (!Guid.TryParseExact(r.Id, "D", out var parsedId) || r.Id != parsedId.ToString("D")) throw new ArgumentException("Invalid transfer ID.");
        if (r.Name.Length > 512 || r.Size < 0 || r.Size > MaxFile) throw new ArgumentException("Files must be at most 2 GB.");
        if (!new[]{"save","open","print","copy","url"}.Contains(r.Action)) throw new ArgumentException("Unknown action.");
        if (r.Copies is < 1 or > 99 || r.Paper.Length > 100 || (r.Printer?.Length ?? 0) > 256 || !new[]{"fit","fill","actual"}.Contains(r.Fit)) throw new ArgumentException("Invalid print options.");
        if (!new[]{"default","simplex","long","short"}.Contains(r.Duplex) || (r.PageFrom != 0 || r.PageTo != 0) && (r.PageFrom < 1 || r.PageTo < r.PageFrom || r.PageTo > 500)) throw new ArgumentException("Invalid duplex or PDF page range.");
        if (r.Action == "print" && r.PageFrom > 0 && Path.GetExtension(SafeName(r.Name)).ToLowerInvariant() != ".pdf") throw new ArgumentException("Page ranges are available for PDF files only.");
        if (r.Action is "copy" or "url") {
            if (r.Size != 0 || string.IsNullOrEmpty(r.Text) || r.Text.Length > 65536) throw new ArgumentException("Invalid text.");
            if (r.Action == "url" && (!Uri.TryCreate(r.Text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo))) throw new ArgumentException("Only HTTP and HTTPS links are supported.");
        } else if (r.Sha256.Length != 64 || !r.Sha256.All(Uri.IsHexDigit)) throw new ArgumentException("SHA-256 required.");
        if (r.Action == "print" && !new[]{".pdf",".jpg",".jpeg",".png",".bmp"}.Contains(Path.GetExtension(SafeName(r.Name)).ToLowerInvariant())) throw new ArgumentException("Print supports PDF, JPG, PNG and BMP.");
    }
}
