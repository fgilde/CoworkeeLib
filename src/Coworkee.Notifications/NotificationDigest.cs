using Coworkee.Application;
using Coworkee.BackgroundJobs;
using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Settings;
using Coworkee.Core.Security;
using Coworkee.Domain;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Mailing;
using Coworkee.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Coworkee.Notifications;

/// <summary>When a user last got a digest, so nothing is mailed twice.</summary>
[NotAudited]
public sealed class NotificationDigestState : Entity
{
    public Guid UserId { get; set; }

    public DateTimeOffset LastSentAt { get; set; }
}

internal sealed class NotificationSettingDefinitions : ISettingDefinitionContributor
{
    public void Define(SettingDefinitionContext context) =>
        context.Group("Notifications", "Notifications")
            .Add(NotificationDigestJob.Setting, "Daily e-mail with unread notifications", SettingType.Bool, [SettingScope.Global, SettingScope.User], "true", visibleToClient: true);
}

/// <summary>Once a day every user with unread notifications since the last digest (at most a day back) gets one mail listing them, unless they switched it off.</summary>
public sealed class NotificationDigestJob(
    CoworkeeDbContext db, IMailSender mails, IUserDirectory users, IDistributedLock locks, IOptions<NotificationOptions> options, TimeProvider clock, ILogger<NotificationDigestJob> logger) : IRecurringJob
{
    public const string Id = "coworkee-notification-digest";
    public const string Setting = "Notifications.Digest";
    public const string Template = "Notifications.Digest";

    private const int PageSize = 500;
    private const int MaxItems = 50;

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        // a run that is already going (manual trigger during the scheduled one) mails everything this one would
        await using var held = await locks.AcquireAsync(Id, TimeSpan.Zero, cancellationToken);
        if (held is null)
        {
            return;
        }

        var now = clock.GetUtcNow();
        var since = now.AddDays(-1);

        // bool settings may be stored in any casing
        var globallyOff = await db.Set<SettingValue>().AnyAsync(v => v.Name == Setting && v.Scope == SettingScope.Global && v.Value!.ToLower() == "false", cancellationToken);
        var optedIn = db.Set<SettingValue>().Where(v => v.Name == Setting && v.Scope == SettingScope.User && v.Value!.ToLower() == "true").Select(v => v.ScopeKey);
        var optedOut = db.Set<SettingValue>().Where(v => v.Name == Setting && v.Scope == SettingScope.User && v.Value!.ToLower() == "false").Select(v => v.ScopeKey);

        // notifications of the window up to now: those created while the job runs go into tomorrow's digest
        var unread = db.Set<Notification>().AsNoTracking().Where(n => n.ReadAt == null && n.CreatedAt > since && n.CreatedAt <= now);
        unread = globallyOff ? unread.Where(n => optedIn.Contains(n.UserId)) : unread.Where(n => !optedOut.Contains(n.UserId));

        // every recipient in pages of users, so nobody is starved behind a cap
        Guid? after = null;
        while (await unread.Select(n => n.UserId).Distinct().Where(id => after == null || id.CompareTo(after.Value) > 0).OrderBy(id => id).Take(PageSize).ToListAsync(cancellationToken) is { Count: > 0 } page)
        {
            after = page[^1];
            await SendPageAsync(page, unread, since, now, cancellationToken);
        }
    }

    private async Task SendPageAsync(List<Guid> userIds, IQueryable<Notification> unread, DateTimeOffset since, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var states = await db.Set<NotificationDigestState>().Where(s => userIds.Contains(s.UserId)).ToDictionaryAsync(s => s.UserId, cancellationToken);
        var emails = await users.GetActiveEmailsAsync(userIds, cancellationToken);
        var names = await users.GetDisplayNamesAsync(userIds, cancellationToken);
        var pending = (await unread.Where(n => userIds.Contains(n.UserId)).OrderByDescending(n => n.CreatedAt).ToListAsync(cancellationToken)).ToLookup(n => n.UserId);
        foreach (var userId in userIds)
        {
            var last = states.TryGetValue(userId, out var state) && state.LastSentAt > since ? state.LastSentAt : since;
            var items = pending[userId].Where(n => n.CreatedAt > last).Take(MaxItems).ToList();
            if (items.Count == 0 || !emails.TryGetValue(userId, out var email))
            {
                continue;
            }

            try
            {
                // ponytail: one mail per user through the global mail settings and the default language; tenant mail settings and user languages would need both on the queued mail
                await mails.QueueAsync(email, Template, new
                {
                    user = new { first_name = names.GetValueOrDefault(userId) ?? email, email },
                    notifications = items.Select(n => new { title = n.Title, body = n.Body, link = Absolute(n.Link) }).ToList(),
                }, null, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // this user's mail is left out; the others of the page still go
                logger.LogWarning(exception, "The notification digest for {UserId} could not be queued.", userId);
                foreach (var entry in db.ChangeTracker.Entries().Where(e => e.State == EntityState.Added && e.Entity is not NotificationDigestState).ToList())
                {
                    entry.State = EntityState.Detached;
                }

                continue;
            }

            if (state is null)
            {
                state = new NotificationDigestState { UserId = userId };
                db.Add(state);
                states[userId] = state;
            }

            state.LastSentAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Server-made links are relative paths; they are made absolute, anything that is not a path or http(s) is dropped.</summary>
    private string? Absolute(string? link) => link switch
    {
        null or "" => null,
        _ when link.StartsWith('/') && !link.StartsWith("//", StringComparison.Ordinal) =>
            options.Value.PublicAppUrl is { Length: > 0 } root ? root.TrimEnd('/') + link : link,
        _ when Uri.TryCreate(link, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) => link,
        _ => null,
    };
}
