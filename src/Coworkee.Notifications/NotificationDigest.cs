using Coworkee.BackgroundJobs;
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

public sealed class NotificationOptions
{
    public const string Section = "Coworkee:Notifications";

    /// <summary>Base of the links in digest mails (the web app); without it the links stay relative.</summary>
    public string? PublicAppUrl { get; set; }

    /// <summary>When the daily digest goes out (cron, UTC).</summary>
    public string DigestCron { get; set; } = "0 6 * * *";
}

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
    CoworkeeDbContext db, IMailSender mails, IUserDirectory users, IOptions<NotificationOptions> options, TimeProvider clock, ILogger<NotificationDigestJob> logger) : IRecurringJob
{
    public const string Id = "coworkee-notification-digest";
    public const string Setting = "Notifications.Digest";
    public const string Template = "Notifications.Digest";

    private const int MaxUsers = 2000;
    private const int MaxItems = 50;

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var since = now.AddDays(-1);
        var globallyOff = await db.Set<SettingValue>().AnyAsync(v => v.Name == Setting && v.Scope == SettingScope.Global && v.Value == "false", cancellationToken);
        var optedIn = db.Set<SettingValue>().Where(v => v.Name == Setting && v.Scope == SettingScope.User && v.Value == "true").Select(v => v.ScopeKey);
        var optedOut = db.Set<SettingValue>().Where(v => v.Name == Setting && v.Scope == SettingScope.User && v.Value == "false").Select(v => v.ScopeKey);

        // the user's own choice wins over the global default
        var unread = db.Set<Notification>().AsNoTracking().Where(n => n.ReadAt == null && n.CreatedAt > since);
        unread = globallyOff ? unread.Where(n => optedIn.Contains(n.UserId)) : unread.Where(n => !optedOut.Contains(n.UserId));
        var userIds = await unread.Select(n => n.UserId).Distinct().Take(MaxUsers).ToListAsync(cancellationToken);
        var states = await db.Set<NotificationDigestState>().Where(s => userIds.Contains(s.UserId)).ToDictionaryAsync(s => s.UserId, cancellationToken);
        var names = await users.GetDisplayNamesAsync(userIds, cancellationToken);
        foreach (var userId in userIds)
        {
            var after = states.TryGetValue(userId, out var state) && state.LastSentAt > since ? state.LastSentAt : since;
            var items = await unread.Where(n => n.UserId == userId && n.CreatedAt > after).OrderByDescending(n => n.CreatedAt).Take(MaxItems).ToListAsync(cancellationToken);
            if (items.Count == 0 || await users.GetEmailAsync(userId, cancellationToken) is not { Length: > 0 } email)
            {
                continue;
            }

            try
            {
                // ponytail: one mail per user through the global mail settings; tenant mail settings would need the tenant on the queued mail
                await mails.QueueAsync(email, Template, new
                {
                    user = new { first_name = names.GetValueOrDefault(userId) ?? email, email },
                    notifications = items.Select(n => new { title = n.Title, body = n.Body, link = Absolute(n.Link) }).ToList(),
                }, null, cancellationToken);
                if (state is null)
                {
                    state = new NotificationDigestState { UserId = userId };
                    db.Add(state);
                    states[userId] = state;
                }

                state.LastSentAt = now;
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "The notification digest for {UserId} could not be queued.", userId);
                db.ChangeTracker.Clear();
            }
        }
    }

    private string? Absolute(string? link) =>
        link is { Length: > 0 } && options.Value.PublicAppUrl is { Length: > 0 } root && !Uri.IsWellFormedUriString(link, UriKind.Absolute)
            ? root.TrimEnd('/') + "/" + link.TrimStart('/')
            : link;
}
