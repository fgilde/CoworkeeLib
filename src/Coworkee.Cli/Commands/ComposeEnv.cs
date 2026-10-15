using System.Buffers.Text;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Coworkee.Cli.Commands;

/// <summary>
/// The .env that <c>aspire publish</c> writes next to docker-compose.yaml: every parameter, image and port with a comment above it.
/// Empty values get what a single host needs; values of an earlier run are kept, so passwords and certificates stay the same.
/// </summary>
internal static class ComposeEnv
{
    private const string ImageComment = "# Container image name for ";

    public static Dictionary<string, string> Values(string env) =>
        Lines(env).Where(l => l.Key is not null).GroupBy(l => l.Key!).ToDictionary(g => g.Key, g => g.Last().Value!);

    /// <summary>The images the file names (<c>shop-api</c>), from the comments above the image variables.</summary>
    public static IEnumerable<string> Images(string env) =>
        Lines(env).Where(l => l.Comment?.StartsWith(ImageComment, StringComparison.Ordinal) == true && l.Key is not null).Select(l => l.Comment![ImageComment.Length..].Trim());

    public static string Fill(string env, IReadOnlyDictionary<string, string> previous, string appName)
    {
        var vapid = new Lazy<(string Public, string Private)>(VapidKeys);
        return string.Join('\n', Lines(env).Select(l => l.Key is null || l.Value!.Length > 0 ? l.Text
            : $"{l.Key}={(previous.GetValueOrDefault(l.Key) is { Length: > 0 } kept ? kept : Default(l.Key, l.Comment, appName, vapid))}"));
    }

    private static string Default(string key, string? comment, string appName, Lazy<(string Public, string Private)> vapid) => key switch
    {
        _ when key.EndsWith("_PUSH_PUBLIC_KEY", StringComparison.Ordinal) => vapid.Value.Public,
        _ when key.EndsWith("_PUSH_PRIVATE_KEY", StringComparison.Ordinal) => vapid.Value.Private,
        _ when comment?.StartsWith(ImageComment, StringComparison.Ordinal) == true => $"{comment[ImageComment.Length..].Trim()}:latest",
        _ when key.EndsWith("_PORT", StringComparison.Ordinal) => "8080",
        _ when key.EndsWith("_SIGNING_CERTIFICATE", StringComparison.Ordinal) => Certificate($"{appName} token signing", X509KeyUsageFlags.DigitalSignature),
        _ when key.EndsWith("_ENCRYPTION_CERTIFICATE", StringComparison.Ordinal) => Certificate($"{appName} token encryption", X509KeyUsageFlags.KeyEncipherment),
        _ when key.EndsWith("_PASSWORD", StringComparison.Ordinal) || key.EndsWith("_TOKEN", StringComparison.Ordinal) || key.EndsWith("_SECRET", StringComparison.Ordinal) =>
            Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(20)),
        _ => string.Empty,
    };

    /// <summary>A Web Push (VAPID) pair as base64url: the uncompressed P-256 public point and the private scalar.</summary>
    private static (string Public, string Private) VapidKeys()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(true);
        return (Base64Url.EncodeToString([4, .. parameters.Q.X!, .. parameters.Q.Y!]), Base64Url.EncodeToString(parameters.D));
    }

    /// <summary>A self-signed PKCS#12 certificate as Base64, what the auth server signs or encrypts its tokens with.</summary>
    public static string Certificate(string name, X509KeyUsageFlags usage)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(usage, critical: true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(5));
        return Convert.ToBase64String(certificate.Export(X509ContentType.Pfx));
    }

    private static IEnumerable<(string Text, string? Comment, string? Key, string? Value)> Lines(string env)
    {
        string? comment = null;
        foreach (var text in env.Replace("\r\n", "\n").Split('\n'))
        {
            var equals = text.IndexOf('=');
            if (text.StartsWith('#'))
            {
                comment = text;
                yield return (text, null, null, null);
            }
            else if (equals > 0)
            {
                yield return (text, comment, text[..equals].Trim(), text[(equals + 1)..].Trim());
                comment = null;
            }
            else
            {
                yield return (text, null, null, null);
            }
        }
    }
}
