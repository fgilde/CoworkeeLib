using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Coworkee.Identity.Users;

/// <summary>Protects tickets with the key ring all hosts share, so the auth server reads what the API issued; a ticket lives a minute.</summary>
public sealed class ImpersonationTickets(IDataProtectionProvider provider, TimeProvider clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    private readonly ITimeLimitedDataProtector _protector = provider.CreateProtector("Coworkee.Impersonation.v1").ToTimeLimitedDataProtector();

    public string Issue(ImpersonationTicket ticket) => _protector.Protect(JsonSerializer.Serialize(ticket), clock.GetUtcNow() + Lifetime);

    /// <summary>The ticket, or null when it is expired, altered or no ticket at all.</summary>
    public ImpersonationTicket? Read(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ImpersonationTicket>(_protector.Unprotect(value));
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or FormatException)
        {
            return null;
        }
    }
}
