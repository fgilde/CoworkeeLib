using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Notifications;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Coworkee.Webhooks;

/// <summary>Tells the endpoint's owner and the organisation's administrators that an endpoint was switched off.</summary>
internal sealed partial class WebhookAdmins(CoworkeeDbContext db, IServiceProvider services, ILogger<WebhookAdmins> logger)
{
    public const string NotificationType = "webhooks.disabled";

    public async Task NotifyDisabledAsync(WebhookSubscription subscription, CancellationToken cancellationToken)
    {
        LogDisabled(subscription.Url, subscription.ConsecutiveFailures);
        if (services.GetService<INotifier>() is not { } notifier)
        {
            return;
        }

        // ponytail: admins are the holders of the system admin role; holders of Webhooks.Manage through other roles only hear of it as owner
        var tenantId = subscription.TenantId;
        var adminRoles = db.Set<Role>().IgnoreQueryFilters().Where(r => r.IsSystem && r.Name == SystemRoles.Admin && (r.TenantId == null || r.TenantId == tenantId)).Select(r => r.Id);
        var withRole = db.Set<IdentityUserRole<Guid>>().Where(r => adminRoles.Contains(r.RoleId)).Select(r => r.UserId);
        var admins = await db.Set<User>().IgnoreQueryFilters().Where(u => u.TenantId == tenantId && u.IsActive && withRole.Contains(u.Id)).Select(u => u.Id).ToListAsync(cancellationToken);
        await notifier.NotifyLocalizedAsync([subscription.OwnerId, .. admins], NotificationType, "Webhook endpoint switched off",
            "{0} failed {1} times in a row and was switched off.", [subscription.Url, subscription.ConsecutiveFailures.ToString(System.Globalization.CultureInfo.InvariantCulture)],
            "/admin/webhooks", cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Webhook endpoint {Url} failed {Failures} times in a row and was switched off")]
    private partial void LogDisabled(string url, int failures);
}
