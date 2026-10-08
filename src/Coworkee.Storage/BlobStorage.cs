using System.Globalization;
using System.Security.Cryptography;
using Coworkee.Contracts.Configuration;
using Microsoft.AspNetCore.DataProtection;

namespace Coworkee.Storage;

/// <summary>Stores opaque blobs by key; keys come from <see cref="BlobKeys"/> and are validated by every provider.</summary>
public interface IBlobStorage
{
    Task PutAsync(string key, Stream content, string? contentType, CancellationToken cancellationToken);

    /// <summary>Opens the blob as a seekable stream with a known length, or returns null when it does not exist.</summary>
    Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken);

    /// <summary>Removes every blob whose key starts with <paramref name="prefix"/> followed by '/'.</summary>
    Task DeletePrefixAsync(string prefix, CancellationToken cancellationToken);

    /// <summary>Removes the blob; missing blobs are ignored.</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken);
}

public static class BlobKeys
{
    /// <summary>A fresh key <c>{tenant}/{yyyy}/{MM}/{guid}</c>.</summary>
    public static string New(Guid tenantId, TimeProvider clock)
    {
        var now = clock.GetUtcNow();
        return $"{tenantId:N}/{now:yyyy}/{now:MM}/{Guid.CreateVersion7():N}";
    }

    /// <summary>Segments of lower ASCII letters, digits, '-', '_' and '.', separated by single '/', never '.' or '..'.</summary>
    public static string Validate(string key)
    {
        var segments = key.Split('/');
        if (key.Length is 0 or > 512 || segments.Any(s => s.Length == 0 || s is "." or ".." || !s.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')))
        {
            throw new ArgumentException($"'{key}' is not a valid blob key.", nameof(key));
        }

        return key;
    }
}

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
