using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Coworkee.Storage;

/// <summary>Short lived, tamper proof tokens that stand for a blob key, for download links without a session.</summary>
public sealed class BlobLinks(IDataProtectionProvider protection, TimeProvider clock)
{
    private readonly IDataProtector _protector = protection.CreateProtector("Coworkee.Storage.BlobLinks");

    public string Create(string key, TimeSpan lifetime) =>
        _protector.Protect(clock.GetUtcNow().Add(lifetime).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) + "|" + BlobKeys.Validate(key));

    public bool TryResolve(string token, out string key)
    {
        key = string.Empty;
        try
        {
            var payload = _protector.Unprotect(token);
            var separator = payload.IndexOf('|', StringComparison.Ordinal);
            if (separator < 0 || !long.TryParse(payload[..separator], CultureInfo.InvariantCulture, out var expires) || clock.GetUtcNow().ToUnixTimeSeconds() > expires)
            {
                return false;
            }

            key = payload[(separator + 1)..];
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
