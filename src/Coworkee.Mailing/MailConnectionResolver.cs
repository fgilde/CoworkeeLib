using Coworkee.Contracts.Mailing;
using Coworkee.Contracts.Settings;
using Coworkee.Core.Security;
using Coworkee.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Mailing;

internal sealed class MailConnectionResolver(ISettingProvider settings, ICurrentUser currentUser, IServiceProvider services)
{
    private static readonly string[] Names =
    [
        MailSettings.Host, MailSettings.Port, MailSettings.UseSsl, MailSettings.UserName, MailSettings.Password,
        MailSettings.ApiKey, MailSettings.MailgunDomain, MailSettings.MailgunRegion, MailSettings.From, MailSettings.Whitelist,
    ];

    public async Task<MailConnection> ResolveAsync(CancellationToken cancellationToken)
    {
        var scope = await ScopeAsync(cancellationToken);
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var name in Names)
        {
            values[name] = await settings.GetForScopeAsync(name, scope, cancellationToken);
        }

        var provider = await settings.GetForScopeAsync(MailSettings.Provider, scope, cancellationToken) ?? MailProviders.Smtp;
        var missing = provider switch
        {
            MailProviders.Smtp when Blank(values[MailSettings.Host]) => "SMTP host",
            MailProviders.SendGrid or MailProviders.Mailchimp when Blank(values[MailSettings.ApiKey]) => "Mail API key",
            MailProviders.Mailgun when Blank(values[MailSettings.ApiKey]) => "Mail API key",
            MailProviders.Mailgun when Blank(values[MailSettings.MailgunDomain]) => "Mailgun domain",
            _ => null,
        };
        return missing is null ? new MailConnection(provider, values) : throw new InvalidOperationException($"{missing} is not configured.");
    }

    private async Task<SettingScope> ScopeAsync(CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId
            || (services.GetService<ITenantDirectory>() is { } tenants && await tenants.IsSystemTenantAsync(tenantId, cancellationToken)))
        {
            return SettingScope.Global;
        }

        return bool.TryParse(await settings.GetForScopeAsync(MailSettings.UseOwnServer, SettingScope.Tenant, cancellationToken), out var own) && own
            ? SettingScope.Tenant
            : SettingScope.Global;
    }

    private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);
}
