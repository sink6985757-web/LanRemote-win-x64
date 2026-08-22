using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace LanRemote.Core;

public static class EphemeralCertificate
{
    public static X509Certificate2 Create()
    {
        using RSA rsa = RSA.Create(2048);
        CertificateRequest request = new(
            "CN=LanRemote Ephemeral Session",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        DateTimeOffset now = DateTimeOffset.UtcNow;
        using X509Certificate2 temporary = request.CreateSelfSigned(now.AddMinutes(-5), now.AddDays(1));
        return X509CertificateLoader.LoadPkcs12(
            temporary.Export(X509ContentType.Pkcs12),
            password: null,
            // Windows Schannel cannot authenticate with EphemeralKeySet. This
            // session-only user key is not persisted in a certificate store and
            // is released when the host object closes.
            X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable);
    }
}
