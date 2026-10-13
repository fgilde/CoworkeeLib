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

    private static readonly HashSet<string> Secrets = [MailSettings.Password, MailSettings.ApiKey];

    public async Task<MailConnection> ResolveAsync(CancellationToken cancellationToken) =>
        await ResolveAsync(await ScopeAsync(cancellationToken), null, cancellationToken);

    /// <summary>The unsaved form of the current scope (system organisation: Global, else the organisation's own); an empty secret keeps the stored one unless it is listed as cleared.</summary>
    public async Task<MailConnection> ResolveDraftAsync(MailConnectionTestRequest request, CancellationToken cancellationToken) =>
        await ResolveAsync(await IsSystemAsync(cancellationToken) ? SettingScope.Global : SettingScope.Tenant, request, cancellationToken);

    private async Task<MailConnection> ResolveAsync(SettingScope scope, MailConnectionTestRequest? request, CancellationToken cancellationToken)
    {
        var draft = request?.Values;
        async Task<string?> GetAsync(string name, bool storedOnly = false)
        {
            if (draft is not null && draft.TryGetValue(name, out var typed) && (!Secrets.Contains(name) || !string.IsNullOrWhiteSpace(typed)))
            {
                return string.IsNullOrWhiteSpace(typed) ? null : typed.Trim();
            }

            if (request?.Cleared?.Contains(name) == true)
            {
                return null;
            }

            return await settings.GetForScopeAsync(name, scope, storedOnly, cancellationToken);
        }

        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var name in Names)
        {
            values[name] = await GetAsync(name);
        }

        var provider = await GetAsync(MailSettings.Provider) ?? MailProviders.Smtp;
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
            var value = scope == SettingScope.Tenant ? await GetAsync(name, storedOnly: true) : values[name];
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"{label} is not configured.");
            }
        }

        return new MailConnection(provider, values);
    }

    public async Task<bool> IsSystemAsync(CancellationToken cancellationToken) =>
        currentUser.TenantId is not { } tenantId
        || (services.GetService<ITenantDirectory>() is { } tenants && await tenants.IsSystemTenantAsync(tenantId, cancellationToken));

    private async Task<SettingScope> ScopeAsync(CancellationToken cancellationToken)
    {
        if (await IsSystemAsync(cancellationToken))
        {
            return SettingScope.Global;
        }

        return bool.TryParse(await settings.GetForScopeAsync(MailSettings.UseOwnServer, SettingScope.Tenant, cancellationToken: cancellationToken), out var own) && own
            ? SettingScope.Tenant
            : SettingScope.Global;
    }
}
