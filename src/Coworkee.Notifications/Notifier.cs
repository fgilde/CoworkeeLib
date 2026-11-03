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

    public async Task PublishAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        var type = services.GetService<NotificationTypeOptions>()?.Find(message.Type);
        var recipients = await RecipientsAsync(message, type, cancellationToken);
        if (recipients.Count == 0)
        {
            return;
        }

        var now = clock.GetUtcNow();
        var push = Push.PushConfiguration.IsOn(options.Value.Push);
        List<string>? arguments = message.Arguments is null ? null : [.. message.Arguments];
        var mails = new List<NotificationMails.Item>();
        var model = NotificationMailModel.Json(message.MailModel);
        var delay = type is not null && recipients.Any(r => r.Preference.Email) ? await EmailDelayAsync(type, cancellationToken) : null;

        // a mail goes only to an active address; who has none keeps the notification for the digest
        var reachable = recipients.Any(r => r.Preference.Email) && services.GetService<IUserDirectory>() is { } users
            ? await users.GetActiveEmailsAsync([.. recipients.Where(r => r.Preference.Email).Select(r => r.UserId)], cancellationToken)
            : null;
        foreach (var (userId, preference) in recipients)
        {
            var email = preference.Email && (reachable?.ContainsKey(userId) ?? true);
            if (preference.InApp)
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "The mail delay of the notification type {Type} could not be resolved; its default applies.")]
    private partial void LogDelayFailed(Exception exception, string type);

    /// <summary>Who gets it and how: addressed users unless their scope is Off, followers with All or Followed, the audience with All; never the actor unless addressed.</summary>
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
        else if (followers.Count > 0)
        {
            followers = await StillSeeingAsync(followers, message.EntityType!, message.EntityId!.Value, cancellationToken);
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
        var result = new List<(Guid, NotificationPreference)>();
        foreach (var userId in candidates)
        {
            var preference = chosen.GetValueOrDefault(userId) ?? defaults;
            var wanted = direct.Contains(userId) ? preference.Scope != NotificationScope.Off
                : userId == actor ? false
                : followers.Contains(userId) ? preference.Scope is NotificationScope.All or NotificationScope.Followed
                : preference.Scope == NotificationScope.All;
            if (wanted && (preference.InApp || preference.Email))
            {
                result.Add((userId, preference));
            }
        }

        return result;
    }

    /// <summary>
    /// The followers who still see the entity, each checked as himself against the set of a <c>Followable&lt;TEntity&gt;</c> registration (permission,
    /// row filters, groups). Followers of entity types without that check get nothing without an audience.
    /// </summary>
    private async Task<HashSet<Guid>> StillSeeingAsync(HashSet<Guid> followers, string entityType, Guid entityId, CancellationToken cancellationToken)
    {
        if (services.GetService<NotificationTypeOptions>()?.FollowTargets.GetValueOrDefault(entityType)?.VisibleIdsAsync is not { } visibleIds)
        {
            return [];
        }

        // ponytail: one query per follower; batch by permission set when entities gather hundreds of followers
        var scopes = services.GetRequiredService<IServiceScopeFactory>();
        var seeing = new HashSet<Guid>();
        foreach (var follower in followers)
        {
            using var actor = CurrentUserScope.Begin(new ImpersonatedUser(follower, currentUser.TenantId));
            await using var scope = scopes.CreateAsyncScope();
            if (await visibleIds(scope.ServiceProvider, cancellationToken) is { } ids && await ids.AnyAsync(id => id == entityId, cancellationToken))
            {
                seeing.Add(follower);
            }
        }

        return seeing;
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
