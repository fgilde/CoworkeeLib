using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace Coworkee.Settings;

public sealed class SettingProtector(IDataProtectionProvider provider, ILogger<SettingProtector> logger)
{
    private readonly IDataProtector _protector = provider.CreateProtector("Coworkee.Settings");

    public string Protect(string value) => _protector.Protect(value);

    // a secret stored under another key ring reads as unset instead of failing every read of the settings
    public string? Unprotect(string? value)
    {
        if (value is null)
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(value);
        }
        catch (CryptographicException exception)
        {
            logger.LogWarning(exception, "A stored secret setting cannot be decrypted with the current key ring; it reads as unset until it is saved again");
            return null;
        }
    }
}
