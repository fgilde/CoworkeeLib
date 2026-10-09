using System.Globalization;
using Coworkee.Application;
using Coworkee.Application.Localization;
using Coworkee.BackgroundJobs;
using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Localization;
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
    CoworkeeDbContext db, IMailSender mails, IUserDirectory users, IDistributedLock locks, IOptions<NotificationOptions> options, TimeProvider clock,
    IEnumerable<ITextTranslator> translators, ILogger<NotificationDigestJob> logger) : IRecurringJob
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
        var cultures = await CulturesAsync(userIds, pending, cancellationToken);
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
                // ponytail: one mail per user through the global mail settings; tenant mail settings would need the tenant on the queued mail
                var culture = cultures.GetValueOrDefault(userId);
                await mails.QueueAsync(email, Template, new
                {
                    user = new { first_name = names.GetValueOrDefault(userId) ?? email, email },
                    notifications = items.Select(n => new { title = Text(n.Title, n.Arguments, culture), body = Text(n.Body, n.Arguments, culture), link = Absolute(n.Link) }).ToList(),
                }, culture?.Name, cancellationToken);
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

    /// <summary>Each reader's language: his own setting, else the one of his organisation, else the system's; none leaves the default.</summary>
    private async Task<Dictionary<Guid, CultureInfo?>> CulturesAsync(List<Guid> userIds, ILookup<Guid, Notification> pending, CancellationToken cancellationToken)
    {
        var tenantIds = pending.SelectMany(g => g).Select(n => n.TenantId).OfType<Guid>().Distinct().ToList();
        var values = await db.Set<SettingValue>().AsNoTracking()
            .Where(v => v.Name == LocalizationSettings.Culture && v.Value != null && v.Value != string.Empty)
            .Where(v => v.Scope == SettingScope.Global
                        || (v.Scope == SettingScope.Tenant && tenantIds.Contains(v.ScopeKey!.Value))
                        || (v.Scope == SettingScope.User && userIds.Contains(v.ScopeKey!.Value)))
            .ToListAsync(cancellationToken);
        string? Of(SettingScope scope, Guid? key) => values.FirstOrDefault(v => v.Scope == scope && v.ScopeKey == key)?.Value;
        return userIds.ToDictionary(id => id, id => Culture(
            Of(SettingScope.User, id) ?? Of(SettingScope.Tenant, pending[id].FirstOrDefault()?.TenantId) ?? Of(SettingScope.Global, null)));
    }

    private static CultureInfo? Culture(string? name)
    {
        try
        {
            return name is null ? null : CultureInfo.GetCultureInfo(name);
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Localizable notifications (with arguments) in the reader's language, the others as written.</summary>
    private string? Text(string? text, IReadOnlyList<string>? arguments, CultureInfo? culture) =>
        text is null || arguments is null || culture is null ? Notification.Format(text, arguments) : Notification.Format(translators.Translate(text, culture), arguments);

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
