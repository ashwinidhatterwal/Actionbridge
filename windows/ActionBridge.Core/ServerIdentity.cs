using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
namespace ActionBridge.Core;
public static class ServerIdentity {
    public static byte[] CreatePfx() {
        using var rsa=RSA.Create(2048);
        var request=new CertificateRequest("CN=ActionBridge",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false,false,0,true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature|X509KeyUsageFlags.KeyEncipherment,true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection{new Oid("1.3.6.1.5.5.7.3.1")},false));
        using var certificate=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddYears(10));
        return certificate.Export(X509ContentType.Pfx,"");
    }
    public static X509Certificate2 Load(byte[] pfx) {
        // Schannel needs a stored private key, including on the very first launch.
        // Reimport the existing PFX so the phone's pinned certificate stays the same.
        return X509CertificateLoader.LoadPkcs12(pfx,"",X509KeyStorageFlags.UserKeySet|X509KeyStorageFlags.PersistKeySet);
    }
}
