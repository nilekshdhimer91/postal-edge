using System.Security.Cryptography;
using System.Text;
using DNA.Email.Core.Options;

namespace DNA.Email.Core.Signing;

public sealed class DkimSigner : IDisposable
{
    private readonly DkimOptions _opts;
    private readonly RSA _rsa;

    public DkimSigner(DkimOptions opts)
    {
        _opts = opts;
        _rsa = RSA.Create();
        _rsa.ImportFromPem(_opts.PrivateKeyPem);
    }

    public string ComputeSignatureHeader(Dictionary<string, string> headers, string body)
    {
        var bodyHash = HashBody(body);
        var headersToSign = _opts.HeadersToSign
            .Where(h => headers.ContainsKey(h))
            .ToArray();

        var headerCanon = CanonicalizeHeaders(headers, headersToSign);
        var dkimHeader = BuildDkimHeaderBase(bodyHash, headersToSign);
        var signInput = headerCanon + "dkim-signature:" + RelaxHeader(dkimHeader);

        var sig = _rsa.SignData(
            Encoding.UTF8.GetBytes(signInput),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return $"DKIM-Signature: {dkimHeader}; b={WrapBase64(Convert.ToBase64String(sig))}";
    }

    private string HashBody(string body)
    {
        var canon = body.TrimEnd() + "\r\n";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canon));
        return Convert.ToBase64String(hash);
    }

    private static string CanonicalizeHeaders(Dictionary<string, string> headers, string[] names)
    {
        var sb = new StringBuilder();
        foreach (var name in names)
        {
            if (!headers.TryGetValue(name, out var val)) continue;
            sb.Append($"{name.ToLowerInvariant()}:{RelaxHeader(val)}\r\n");
        }
        return sb.ToString();
    }

    private static string RelaxHeader(string value) =>
        string.Join(" ", value.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)).Trim();

    private string BuildDkimHeaderBase(string bodyHash, string[] signedHeaders) =>
        $"v=1; a=rsa-sha256; c=relaxed/relaxed; d={_opts.Domain}; " +
        $"s={_opts.Selector}; h={string.Join(":", signedHeaders)}; bh={bodyHash}";

    private static string WrapBase64(string b64, int width = 72)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < b64.Length; i += width)
            sb.Append("\r\n  ").Append(b64.AsSpan(i, Math.Min(width, b64.Length - i)));
        return sb.ToString().TrimStart();
    }

    public void Dispose() => _rsa.Dispose();
}
