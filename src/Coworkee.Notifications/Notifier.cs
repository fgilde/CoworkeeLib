using Coworkee.Application.Events;
using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Notifications;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Coworkee.Notifications;

internal sealed partial class Notifier(
    CoworkeeDbContext db, ICurrentUser currentUser, TimeProvider clock, Microsoft.Extensions.Options.IOptions<NotificationOptions> options, IServiceProvider services,
    ILogger<Notifier> logger)
    : INotifier
{
    public Task NotifyAsync(IEnumerable<Guid> userIds, string type, string title, string? body = null, string? link = null, CancellationToken cancellationToken = default) =>
        PublishAsync(new NotificationMessage(type, title, body) { Link = link, Recipients = [.. userIds] }, cancellationToken);

    public Task NotifyLocalizedAsync(
        IEnumerable<Guid> userIds, string type, string title, string? body, IReadOnlyList<string> arguments, string? link = null, CancellationToken cancellationToken = default) =>
        PublishAsync(new NotificationMessage(type, title, body) { Arguments = arguments, Link = link, Recipients = [.. userIds] }, cancellationToken);

    public async Task SendAsync(NotificationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var stored = request.Delivery.HasFlag(Delivery.Stored);
        if (stored || request.Mail != MailDelivery.Never)
        {
            await DeliverAsync(request.ToMessage(await AddressedAsync(request.Target, cancellationToken)), stored, request.Mail, cancellationToken);
        }

        // after the rows: a toast of a unit of work leaves with its commit
        if (request.Delivery.HasFlag(Delivery.Toast))
        {
            await services.GetRequiredService<Realtime.RealtimeEvents>().PublishAsync(request.Target, ToastPayload.Event,
                new ToastPayload(request.Title, request.Body, request.Severity, request.Link, request.Arguments, request.Type), cancellationToken);
        }
    }

    public Task PublishAsync(NotificationMessage message, CancellationToken cancellationToken = default) =>
        DeliverAsync(message, stored: true, MailDelivery.ByPreference, cancellationToken);

    /// <summary>The users the target addresses directly: ids as given (no lookup, as <see cref="NotifyAsync"/>), the rest resolved; the actor only when named.</summary>
    private async Task<IReadOnlyCollection<Guid>> AddressedAsync(EventTarget target, CancellationToken cancellationToken)
    {
        var ids = target is UsersTarget users
            ? users.Ids
            : await (services.GetService<IEventTargetResolver>()
                     ?? throw new InvalidOperationException($"The notification target {target} needs Coworkee.Identity (IEventTargetResolver); only user ids work without it."))
                .UsersAsync(target, cancellationToken: cancellationToken);
        return currentUser.UserId is { } actor && ids.Contains(actor) && !target.Names(actor, actor) ? [.. ids.Where(id => id != actor)] : ids;
    }

    private async Task DeliverAsync(NotificationMessage message, bool stored, MailDelivery mail, CancellationToken cancellationToken)
    {
        var type = services.GetService<NotificationTypeOptions>()?.Find(message.Type);
        var online = mail == MailDelivery.WhenOffline && services.GetService<Realtime.RealtimePresence>() is { } presence
            ? (await presence.OnlineAsync(currentUser.TenantId)).ToHashSet()
            : [];
        var recipients = (await RecipientsAsync(message, type, cancellationToken))
            .Select(r => (r.UserId, InApp: stored && r.Preference.InApp, Email: mail switch
            {
                MailDelivery.Never => false,
                MailDelivery.Always => true,
                MailDelivery.WhenOffline => !online.Contains(r.UserId),
                _ => r.Preference.Email,
            }))
            .Where(r => r.InApp || r.Email)
            .ToList();
        if (recipients.Count == 0)
        {
            return;
        }

        var now = clock.GetUtcNow();
        var push = Push.PushConfiguration.IsOn(options.Value.Push);
        List<string>? arguments = message.Arguments is null ? null : [.. message.Arguments];
        var mails = new List<NotificationMails.Item>();
        var model = NotificationMailModel.Json(message.MailModel);
        var delay = type is not null && recipients.Any(r => r.Email) ? await EmailDelayAsync(type, cancellationToken) : null;

        // a mail goes only to an active address; who has none keeps the notification for the digest
        var reachable = recipients.Any(r => r.Email) && services.GetService<IUserDirectory>() is { } users
            ? await users.GetActiveEmailsAsync([.. recipients.Where(r => r.Email).Select(r => r.UserId)], cancellationToken)
            : null;
        foreach (var (userId, inApp, wantsEmail) in recipients)
        {
            var email = wantsEmail && (reachable?.ContainsKey(userId) ?? true);
            if (inApp)
            {
                var notification = new Notification
                {
                    UserId = userId,
                    TenantId = currentUser.TenantId,
                    Type = message.Type,
                    Category = type?.Category,
                    Title = message.Title,
                    Body = message.Body,
                    Arguments = arguments,
                    Link = message.Link,
                    CreatedAt = now,
                    Mailed = email,
                };
                if (push)
                {
                    notification.OfferPush();
                }

                db.Add(notification);
            }

            if (!email)
            {
                continue;
            }

            if (delay > TimeSpan.Zero)
            {
                Wait(message, model, userId, now, now.Add(delay.Value));
            }
            else
            {
                mails.Add(new NotificationMails.Item(userId, currentUser.TenantId, message.Type, message.Title, message.Body, message.Arguments, message.Link, model));
            }
        }

        await services.GetRequiredService<NotificationMails>().QueueAsync(mails, cancellationToken);
    }

    // a resolver that fails must not fail the command that publishes: the type's static delay holds
    private async Task<TimeSpan?> EmailDelayAsync(NotificationType type, CancellationToken cancellationToken)
    {
        try
        {
            return await type.EmailDelayAsync(services, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogDelayFailed(exception, type.Type);
            return type.EmailDelay;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The entity type {EntityType} has no visibility source: audience and followers skipped. Register it with Followable<TEntity> or send without an entity.")]
    private partial void LogNoVisibilitySource(string entityType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The mail delay of the notification type {Type} could not be resolved; its default applies.")]
    private partial void LogDelayFailed(Exception exception, string type);

    /// <summary>Who may get it (the channels follow from the delivery): addressed users unless their scope is Off, followers with All or Followed, the audience with All, the last two only while they still see the record; never the actor unless addressed.</summary>
    private async Task<List<(Guid UserId, NotificationPreference Preference)>> RecipientsAsync(NotificationMessage message, NotificationType? type, CancellationToken cancellationToken)
    {
        var direct = message.Recipients?.ToHashSet() ?? [];
        var followers = message is { EntityType: { } entityType, EntityId: { } entityId }
            ? (await db.Set<NotificationFollow>().Where(f => f.EntityType == entityType && f.EntityId == entityId).Select(f => f.UserId).ToListAsync(cancellationToken)).ToHashSet()
            : [];
        var audience = message.Audience?.ToHashSet();
        if (audience is not null)
        {
            followers.IntersectWith(audience);
        }

        var candidates = direct.Concat(followers).Concat(audience ?? []).Where(id => id != Guid.Empty).Distinct().ToList();
        var defaults = new NotificationPreference
        {
            Type = message.Type,
            Scope = type?.Scope ?? NotificationScope.All,
            InApp = type?.InApp ?? true,
            Email = type?.Email ?? false,
        };

        // ponytail: the chosen preferences of all candidates in one query; chunk it when audiences reach thousands of users
        var chosen = type is null
            ? []
            : await db.Set<NotificationPreference>().AsNoTracking().Where(p => p.Type == type.Type && candidates.Contains(p.UserId)).ToDictionaryAsync(p => p.UserId, cancellationToken);
        var actor = currentUser.UserId;
        var result = new List<(Guid UserId, NotificationPreference Preference)>();
        foreach (var userId in candidates)
        {
            var preference = chosen.GetValueOrDefault(userId) ?? defaults;
            var wanted = direct.Contains(userId) ? preference.Scope != NotificationScope.Off
                : userId == actor ? false
                : followers.Contains(userId) ? preference.Scope is NotificationScope.All or NotificationScope.Followed
                : preference.Scope == NotificationScope.All;
            if (wanted)
            {
                result.Add((userId, preference));
            }
        }

        // wanted followers and audience members must still see the record (each user checked once, as himself); addressed users are not checked
        var toCheck = result.Where(r => !direct.Contains(r.UserId)).Select(r => r.UserId).ToList();
        if (message is { EntityType: { } checkedType, EntityId: { } checkedId } && toCheck.Count > 0)
        {
            if (services.GetService<NotificationTypeOptions>()?.FollowTargets.GetValueOrDefault(checkedType)?.VisibleIdsAsync is null)
            {
                LogNoVisibilitySource(checkedType);
            }

            var seeing = await services.GetRequiredService<NotificationAudience>().SeeingAsync(toCheck, checkedType, checkedId, cancellationToken);
            result.RemoveAll(r => !direct.Contains(r.UserId) && !seeing.Contains(r.UserId));
        }

        return result;
    }

    /// <summary>
    /// The mail waits until <paramref name="due"/>. It is written by <see cref="NotificationMailWaitingHandler"/> after the command commits (outbox), so a command
    /// that fails sends nothing, and the write can never fail the command.
    /// </summary>
    private void Wait(NotificationMessage message, string? model, Guid userId, DateTimeOffset now, DateTimeOffset due)
    {
        // without an entity nothing debounces: a key of its own
        var key = message is { EntityType: { } entityType, EntityId: { } entityId } ? $"{entityType}:{entityId}" : Guid.NewGuid().ToString("N");
        var waiting = new NotificationMailWaiting(userId, currentUser.TenantId, message.Type, key, message.Title, message.Body, message.Arguments, message.Link, due, model);
        db.Add(new Infrastructure.Outbox.OutboxMessage
        {
            Type = typeof(NotificationMailWaiting).AssemblyQualifiedName!,
            Payload = System.Text.Json.JsonSerializer.Serialize(waiting),
            OccurredAt = now,
            TenantId = currentUser.TenantId,
            ActorId = currentUser.UserId,
            ImpersonatorId = currentUser.ImpersonatorId,
            CorrelationId = System.Diagnostics.Activity.Current?.TraceId.ToHexString(),
        });
    }
}
