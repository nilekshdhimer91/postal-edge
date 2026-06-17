using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using DNA.Email.Core.Options;

namespace DNA.Email.Core.Signing;

public sealed class SmimeSigner : IDisposable
{
    private readonly X509Certificate2 _cert;

    public SmimeSigner(SmimeOptions opts)
    {
        _cert = X509CertificateLoader.LoadPkcs12FromFile(
            opts.CertificatePath,
            opts.CertificatePassword,
            X509KeyStorageFlags.EphemeralKeySet);
    }

    public byte[] Sign(byte[] content)
    {
        var contentInfo = new ContentInfo(content);
        var signedCms = new SignedCms(contentInfo, detached: true);

        var signer = new CmsSigner(_cert)
        {
            DigestAlgorithm = new Oid("2.16.840.1.101.3.4.2.1"), // SHA-256
            IncludeOption = X509IncludeOption.EndCertOnly
        };

        signedCms.ComputeSignature(signer);
        return signedCms.Encode();
    }

    public void Dispose() => _cert.Dispose();
}
