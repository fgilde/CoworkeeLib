using System.Security.Cryptography;
using Coworkee.Contracts.Configuration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Coworkee.Reporting.Subscriptions;

/// <summary>Links of subscription mails: back to the report and to end the subscription without signing in (a protected token names it).</summary>
internal sealed class ReportMailLinks(IDataProtectionProvider provider, IOptions<NotificationOptions> options)
{
    public const string Route = "/api/v1/reports/subscriptions/unsubscribe";

    private readonly IDataProtector _protector = provider.CreateProtector("Coworkee.Reports.Unsubscribe");

    public string Report(Guid reportId) => $"{Root()}/reports/{reportId}";

    public string Unsubscribe(Guid subscriptionId, Guid tenantId) =>
        $"{Root()}{Route}?token={Uri.EscapeDataString(_protector.Protect($"{subscriptionId:N}:{tenantId:N}"))}";

    public bool TryRead(string? token, out Guid subscriptionId, out Guid tenantId)
    {
        subscriptionId = default;
        tenantId = default;
        try
        {
            var parts = string.IsNullOrEmpty(token) ? [] : _protector.Unprotect(token).Split(':');
            return parts.Length == 2 && Guid.TryParseExact(parts[0], "N", out subscriptionId) && Guid.TryParseExact(parts[1], "N", out tenantId);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    // mails are written by a job: there is no request to take the address from
    private string Root() => options.Value.PublicAppUrl is { Length: > 0 } root
        ? root.TrimEnd('/')
        : throw new InvalidOperationException("Set Coworkee:Notifications:PublicAppUrl so report mails can link back to the app.");
}
