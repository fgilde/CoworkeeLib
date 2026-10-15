using System.Text.Json;
using Coworkee.Application.Localization;
using Coworkee.Application.Messaging;
using Coworkee.BackgroundJobs;
using Coworkee.Domain;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Mailing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Coworkee.Notifications.Push;

/// <summary>Raised for each new notification while push is configured; the notification goes to the user's devices after the commit.</summary>
public sealed record NotificationCreated(Guid NotificationId) : IDomainEvent;

internal sealed class NotificationCreatedHandler(IBackgroundJobs jobs) : IDomainEventHandler<NotificationCreated>
{
    public Task HandleAsync(NotificationCreated domainEvent, CancellationToken cancellationToken)
    {
        jobs.Enqueue<PushDeliveryJob, Guid>(domainEvent.NotificationId);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Sends one notification to every device its user subscribed, in the user's language; a click opens its link in the app.
/// Subscriptions the push service no longer knows are removed.
/// </summary>
public sealed partial class PushDeliveryJob(CoworkeeDbContext db, IPushSender sender, IEnumerable<ITextTranslator> translators, ILogger<PushDeliveryJob> logger)
    : IBackgroundJob<Guid>
{
    public async Task ExecuteAsync(Guid notificationId, CancellationToken cancellationToken)
    {
        if (await db.Set<Notification>().AsNoTracking().SingleOrDefaultAsync(n => n.Id == notificationId, cancellationToken) is not { } notification)
        {
            return;
        }

        var devices = await db.Set<PushSubscription>().Where(s => s.UserId == notification.UserId).ToListAsync(cancellationToken);
        if (devices.Count == 0)
        {
            return;
        }

        var culture = (await RecipientCultures.ForAsync(db, new Dictionary<Guid, Guid?> { [notification.UserId] = notification.TenantId }, cancellationToken))[notification.UserId];
        var payload = JsonSerializer.Serialize(new
        {
            title = NotificationText.In(notification.Title, notification.Arguments, culture, translators),
            body = NotificationText.In(notification.Body, notification.Arguments, culture, translators),
            url = AppPath(notification.Link) ?? "/notifications",
            tag = notification.Id,
        }, JsonSerializerOptions.Web);
        foreach (var device in devices)
        {
            var result = await SendAsync(device, payload, cancellationToken);
            if (result == PushResult.Gone)
            {
                db.Remove(device);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Only paths of the app: the service worker opens nothing on another site.</summary>
    internal static string? AppPath(string? link) =>
        link is { Length: > 0 } && link[0] == '/' && !link.StartsWith("//", StringComparison.Ordinal) && !link.StartsWith("/\\", StringComparison.Ordinal) ? link : null;

    private async Task<PushResult> SendAsync(PushSubscription device, string payload, CancellationToken cancellationToken)
    {
        try
        {
            var result = await sender.SendAsync(device, payload, cancellationToken);
            if (result == PushResult.Failed)
            {
                Failed(logger, device.Id, null);
            }

            return result;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or FormatException or ArgumentException or System.Security.Cryptography.CryptographicException)
        {
            Failed(logger, device.Id, exception);
            return PushResult.Failed;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The push to device {DeviceId} failed.")]
    private static partial void Failed(ILogger logger, Guid deviceId, Exception? exception);
}
