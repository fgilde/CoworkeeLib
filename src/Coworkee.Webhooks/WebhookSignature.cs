using System.Security.Cryptography;
using System.Text;

namespace Coworkee.Webhooks;

/// <summary>
/// Signatures in the style of the Standard Webhooks spec: HMAC-SHA256 over "{id}.{timestamp}.{body}", sent as "v1,{base64}" in the
/// webhook-signature header; the key is the base64 part of a "whsec_" secret, any other secret is used as UTF-8 bytes.
/// </summary>
public static class WebhookSignature
{
    public const string SecretPrefix = "whsec_";
    public const string IdHeader = "webhook-id";
    public const string TimestampHeader = "webhook-timestamp";
    public const string SignatureHeader = "webhook-signature";

    public static string NewSecret() => SecretPrefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));

    public static string Sign(string secret, string id, long timestamp, string body) =>
        "v1," + Convert.ToBase64String(HMACSHA256.HashData(Key(secret), Encoding.UTF8.GetBytes($"{id}.{timestamp}.{body}")));

    /// <summary>True when one of the space separated signatures of the header matches and the timestamp is within <paramref name="tolerance"/> of now.</summary>
    public static bool Verify(string secret, string id, string timestamp, string body, string signatureHeader, DateTimeOffset now, TimeSpan tolerance)
    {
        if (!long.TryParse(timestamp, out var seconds) || (now - DateTimeOffset.FromUnixTimeSeconds(seconds)).Duration() > tolerance)
        {
            return false;
        }

        var expected = Encoding.ASCII.GetBytes(Sign(secret, id, seconds, body));
        return signatureHeader.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(signature => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(signature), expected));
    }

    internal static bool IsValidSecret(string secret)
    {
        if (!secret.StartsWith(SecretPrefix, StringComparison.Ordinal))
        {
            return secret.Length >= 16;
        }

        var buffer = new byte[secret.Length];
        return Convert.TryFromBase64String(secret[SecretPrefix.Length..], buffer, out var length) && length >= 16;
    }

    private static byte[] Key(string secret) =>
        secret.StartsWith(SecretPrefix, StringComparison.Ordinal) ? Convert.FromBase64String(secret[SecretPrefix.Length..]) : Encoding.UTF8.GetBytes(secret);
}
