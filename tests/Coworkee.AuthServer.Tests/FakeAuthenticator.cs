using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Coworkee.AuthServer.Tests;

/// <summary>A software authenticator (ES256, attestation "none") that answers the options of the account pages like a browser would.</summary>
public sealed class FakeAuthenticator(byte[]? credentialId = null)
{
    public const string Origin = "http://localhost";

    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private uint _signCount;

    public byte[] CredentialId { get; } = credentialId ?? RandomNumberGenerator.GetBytes(32);

    public string UserHandle { get; private set; } = string.Empty;

    public string Create(string optionsJson)
    {
        var options = JsonDocument.Parse(optionsJson).RootElement;
        UserHandle = options.GetProperty("user").GetProperty("id").GetString()!;
        var parameters = _key.ExportParameters(false);
        byte[] coseKey = [0xA5, 0x01, 0x02, 0x03, 0x26, 0x20, 0x01, 0x21, 0x58, 0x20, .. parameters.Q.X!, 0x22, 0x58, 0x20, .. parameters.Q.Y!];
        byte[] authData =
        [
            .. RpIdHash(options.GetProperty("rp").GetProperty("id").GetString()!), 0x45, 0, 0, 0, 0, .. new byte[16],
            (byte)(CredentialId.Length >> 8), (byte)CredentialId.Length, .. CredentialId, .. coseKey,
        ];
        byte[] attestation = [0xA3, .. Text("fmt"), .. Text("none"), .. Text("attStmt"), 0xA0, .. Text("authData"), 0x59, (byte)(authData.Length >> 8), (byte)authData.Length, .. authData];
        return JsonSerializer.Serialize(new
        {
            id = Base64Url.EncodeToString(CredentialId),
            rawId = Base64Url.EncodeToString(CredentialId),
            type = "public-key",
            response = new
            {
                clientDataJSON = Base64Url.EncodeToString(ClientData("webauthn.create", options)),
                attestationObject = Base64Url.EncodeToString(attestation),
                transports = new[] { "internal" },
            },
            clientExtensionResults = new { },
        });
    }

    public string Get(string optionsJson, bool userVerified = true)
    {
        var options = JsonDocument.Parse(optionsJson).RootElement;
        byte[] authData = [.. RpIdHash(options.GetProperty("rpId").GetString()!), (byte)(userVerified ? 0x05 : 0x01), .. BigEndian(++_signCount)];
        var clientData = ClientData("webauthn.get", options);
        var signature = _key.SignData([.. authData, .. SHA256.HashData(clientData)], HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        return JsonSerializer.Serialize(new
        {
            id = Base64Url.EncodeToString(CredentialId),
            rawId = Base64Url.EncodeToString(CredentialId),
            type = "public-key",
            response = new
            {
                clientDataJSON = Base64Url.EncodeToString(clientData),
                authenticatorData = Base64Url.EncodeToString(authData),
                signature = Base64Url.EncodeToString(signature),
                userHandle = UserHandle,
            },
            clientExtensionResults = new { },
        });
    }

    private static byte[] ClientData(string type, JsonElement options) => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
    {
        type,
        challenge = options.GetProperty("challenge").GetString(),
        origin = Origin,
        crossOrigin = false,
    }));

    private static byte[] RpIdHash(string rpId) => SHA256.HashData(Encoding.UTF8.GetBytes(rpId));

    private static byte[] Text(string value) => [(byte)(0x60 | value.Length), .. Encoding.UTF8.GetBytes(value)];

    private static byte[] BigEndian(uint value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];
}
