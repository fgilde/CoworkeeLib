using Microsoft.AspNetCore.DataProtection;

namespace Coworkee.Webhooks;

internal sealed class WebhookSecrets(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("Coworkee.Webhooks");

    public string Protect(string secret) => _protector.Protect(secret);

    public string Unprotect(string protectedSecret) => _protector.Unprotect(protectedSecret);
}
