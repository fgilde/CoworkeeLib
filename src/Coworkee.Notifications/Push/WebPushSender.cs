using System.Buffers.Binary;
using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Coworkee.Contracts.Configuration;
using Microsoft.Extensions.Options;

namespace Coworkee.Notifications.Push;

public enum PushResult
{
    Sent,

    /// <summary>The push service no longer knows the subscription: the device unsubscribed or the browser data was cleared.</summary>
    Gone,

    Failed,
}

/// <summary>Delivers an encrypted payload to one push subscription; replace it to send through a service of your own.</summary>
public interface IPushSender
{
    Task<PushResult> SendAsync(PushSubscription subscription, string payload, CancellationToken cancellationToken);
}

/// <summary>Web Push with the standard library only: payload encryption by RFC 8291 (aes128gcm) and VAPID by RFC 8292 (ES256).</summary>
public sealed class WebPushSender(HttpClient http, IOptions<NotificationOptions> options) : IPushSender
{
    private const int RecordSize = 4096;

    public async Task<PushResult> SendAsync(PushSubscription subscription, string payload, CancellationToken cancellationToken)
    {
        var push = options.Value.Push;
        var endpoint = new Uri(subscription.Endpoint);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new ByteArrayContent(Encrypt(Encoding.UTF8.GetBytes(payload), Base64Url.DecodeFromChars(subscription.P256dh), Base64Url.DecodeFromChars(subscription.Auth))),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        request.Content.Headers.ContentEncoding.Add("aes128gcm");
        request.Headers.Add("TTL", "86400");
        request.Headers.Authorization = new AuthenticationHeaderValue("vapid",
            $"t={VapidToken(endpoint, Subject(push), push.PublicKey!, push.PrivateKey!, DateTimeOffset.UtcNow)}, k={push.PublicKey}");
        using var response = await http.SendAsync(request, cancellationToken);
        return response.StatusCode switch
        {
            HttpStatusCode.NotFound or HttpStatusCode.Gone => PushResult.Gone,
            _ when response.IsSuccessStatusCode => PushResult.Sent,
            _ => PushResult.Failed,
        };
    }

    private string Subject(PushOptions push) =>
        push.Subject is { Length: > 0 } subject ? subject
        : options.Value.PublicAppUrl is { Length: > 0 } app && app.StartsWith("https:", StringComparison.OrdinalIgnoreCase) ? app
        : "mailto:push@example.invalid";

    /// <summary>The message body of RFC 8291: salt, record size, the sender's public key, then the payload sealed with a key only the browser can derive.</summary>
    public static byte[] Encrypt(byte[] plaintext, byte[] browserPublicKey, byte[] authSecret)
    {
        using var browser = ECDiffieHellman.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = Point(browserPublicKey) });
        using var sender = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var senderPublicKey = Uncompressed(sender.ExportParameters(false).Q);
        var shared = sender.DeriveRawSecretAgreement(browser.PublicKey);

        var salt = RandomNumberGenerator.GetBytes(16);
        var (key, nonce) = ContentKeys(shared, authSecret, browserPublicKey, senderPublicKey, salt);

        // one record: the payload, then the delimiter of the last record
        var padded = new byte[plaintext.Length + 1];
        plaintext.CopyTo(padded, 0);
        padded[^1] = 2;
        var sealedRecord = new byte[padded.Length + 16];
        using (var aes = new AesGcm(key, 16))
        {
            aes.Encrypt(nonce, padded, sealedRecord.AsSpan(0, padded.Length), sealedRecord.AsSpan(padded.Length));
        }

        var body = new byte[16 + 4 + 1 + senderPublicKey.Length + sealedRecord.Length];
        salt.CopyTo(body, 0);
        BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(16), RecordSize);
        body[20] = (byte)senderPublicKey.Length;
        senderPublicKey.CopyTo(body, 21);
        sealedRecord.CopyTo(body, 21 + senderPublicKey.Length);
        return body;
    }

    /// <summary>Content encryption key and nonce of RFC 8291 from the ECDH secret, the browser's auth secret and both public keys.</summary>
    public static (byte[] Key, byte[] Nonce) ContentKeys(byte[] shared, byte[] authSecret, byte[] browserPublicKey, byte[] senderPublicKey, byte[] salt)
    {
        var keyInfo = Encoding.ASCII.GetBytes("WebPush: info\0").Concat(browserPublicKey).Concat(senderPublicKey).ToArray();
        var ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, shared, 32, authSecret, keyInfo);
        return (HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 16, salt, Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0")),
            HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 12, salt, Encoding.ASCII.GetBytes("Content-Encoding: nonce\0")));
    }

    /// <summary>The VAPID JWT for the push service of <paramref name="endpoint"/>, valid for twelve hours.</summary>
    public static string VapidToken(Uri endpoint, string subject, string publicKey, string privateKey, DateTimeOffset now)
    {
        using var key = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = Point(Base64Url.DecodeFromChars(publicKey)),
            D = Base64Url.DecodeFromChars(privateKey),
        });
        var header = Base64Url.EncodeToString("""{"typ":"JWT","alg":"ES256"}"""u8);
        var claims = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["aud"] = endpoint.GetLeftPart(UriPartial.Authority),
            ["exp"] = now.AddHours(12).ToUnixTimeSeconds(),
            ["sub"] = subject,
        }));
        var signature = key.SignData(Encoding.ASCII.GetBytes($"{header}.{claims}"), HashAlgorithmName.SHA256);
        return $"{header}.{claims}.{Base64Url.EncodeToString(signature)}";
    }

    /// <summary>A new VAPID key pair as base64url: the uncompressed public point and the private scalar.</summary>
    public static (string PublicKey, string PrivateKey) CreateKeys()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(true);
        return (Base64Url.EncodeToString(Uncompressed(parameters.Q)), Base64Url.EncodeToString(parameters.D));
    }

    public static ECPoint Point(byte[] uncompressed) =>
        uncompressed is [4, ..] && uncompressed.Length == 65
            ? new ECPoint { X = uncompressed[1..33], Y = uncompressed[33..] }
            : throw new ArgumentException("Not an uncompressed P-256 public key.", nameof(uncompressed));

    public static byte[] Uncompressed(ECPoint point) => [4, .. point.X!, .. point.Y!];
}
