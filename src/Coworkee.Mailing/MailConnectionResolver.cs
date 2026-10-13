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
            values[name] = await settings.GetForScopeAsync(name, scope, cancellationToken: cancellationToken);
        }

        var provider = await settings.GetForScopeAsync(MailSettings.Provider, scope, cancellationToken: cancellationToken) ?? MailProviders.Smtp;
        (string Name, string Label)[] required = provider switch
        {
            MailProviders.Smtp => [(MailSettings.Host, "SMTP host")],
            MailProviders.SendGrid or MailProviders.Mailchimp => [(MailSettings.ApiKey, "Mail API key")],
            MailProviders.Mailgun => [(MailSettings.ApiKey, "Mail API key"), (MailSettings.MailgunDomain, "Mailgun domain")],
            _ => [],
        };
        foreach (var (name, label) in required)
        {
            // an organisation's own server must be entered explicitly, deployment defaults would send through the system's server
            var value = scope == SettingScope.Tenant ? await settings.GetForScopeAsync(name, scope, storedOnly: true, cancellationToken) : values[name];
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"{label} is not configured.");
            }
        }

        return new MailConnection(provider, values);
    }

    private async Task<SettingScope> ScopeAsync(CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId
            || (services.GetService<ITenantDirectory>() is { } tenants && await tenants.IsSystemTenantAsync(tenantId, cancellationToken)))
        {
            return SettingScope.Global;
        }

        return bool.TryParse(await settings.GetForScopeAsync(MailSettings.UseOwnServer, SettingScope.Tenant, cancellationToken: cancellationToken), out var own) && own
            ? SettingScope.Tenant
            : SettingScope.Global;
    }
}
