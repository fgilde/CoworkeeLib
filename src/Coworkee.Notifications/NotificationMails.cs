using Coworkee.Application;
using Coworkee.Application.Localization;
using Coworkee.BackgroundJobs;
using Coworkee.Contracts.Configuration;
using Coworkee.Core.Security;
using Coworkee.Domain;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Mailing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Coworkee.Notifications;

/// <summary>A notification mail that waits for its type's delay; a newer one of the same user, type and entity replaces it.</summary>
[NotAudited]
public sealed class PendingNotificationMail : Entity
{
    public Guid UserId { get; set; }

    public Guid? TenantId { get; set; }

    public required string Type { get; set; }

    /// <summary>The entity ("Shipments:{id}") that debounces; null waits on its own.</summary>
    public string? Key { get; set; }

    public required string Title { get; set; }

    public string? Body { get; set; }

    public List<string>? Arguments { get; set; }

    public string? Link { get; set; }

    public DateTimeOffset DueAt { get; set; }
}

/// <summary>Writes one notification as mail in the reader's language (template <see cref="Template"/>); the mail joins the current unit of work.</summary>
internal sealed class NotificationMails(
    CoworkeeDbContext db, IMailSender mails, IUserDirectory users, IEnumerable<ITextTranslator> translators, IOptions<NotificationOptions> options)
{
    public const string Template = "Notifications.Notification";

    public sealed record Item(Guid UserId, Guid? TenantId, string Title, string? Body, IReadOnlyList<string>? Arguments, string? Link);

    public async Task QueueAsync(IReadOnlyCollection<Item> items, CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return;
        }

        var userIds = items.Select(i => i.UserId).Distinct().ToList();
        var emails = await users.GetActiveEmailsAsync(userIds, cancellationToken);
        var names = await users.GetDisplayNamesAsync(userIds, cancellationToken);
        var cultures = await RecipientCultures.ForAsync(db, items.GroupBy(i => i.UserId).ToDictionary(g => g.Key, g => g.First().TenantId), cancellationToken);
        foreach (var item in items)
        {
            if (!emails.TryGetValue(item.UserId, out var email))
            {
                continue;
            }

            var culture = cultures.GetValueOrDefault(item.UserId);
            await mails.QueueAsync(email, Template, new
            {
                user = new { first_name = names.GetValueOrDefault(item.UserId) ?? email, email },
                notification = new
                {
                    title = NotificationText.In(item.Title, item.Arguments, culture, translators),
                    body = NotificationText.In(item.Body, item.Arguments, culture, translators),
                    link = NotificationLinks.Absolute(item.Link, options.Value.PublicAppUrl),
                },
            }, culture?.Name, cancellationToken);
        }
    }
}

/// <summary>Every minute: the waiting notification mails whose delay is over.</summary>
internal sealed class NotificationMailJob(CoworkeeDbContext db, NotificationMails mails, IDistributedLock locks, TimeProvider clock) : IRecurringJob
{
    public const string Id = "coworkee-notification-mails";
    public const string Cron = "* * * * *";

    // ponytail: at most this many mails per run; the rest follow a minute later
    private const int Batch = 500;

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await using var held = await locks.AcquireAsync(Id, TimeSpan.Zero, cancellationToken);
        if (held is null)
        {
            return;
        }

        var now = clock.GetUtcNow();
        var due = await db.Set<PendingNotificationMail>().Where(m => m.DueAt <= now).OrderBy(m => m.DueAt).Take(Batch).ToListAsync(cancellationToken);
        if (due.Count == 0)
        {
            return;
        }

        // ponytail: one mail per notification through the global mail settings, as the digest
        await mails.QueueAsync([.. due.Select(m => new NotificationMails.Item(m.UserId, m.TenantId, m.Title, m.Body, m.Arguments, m.Link))], cancellationToken);
        db.RemoveRange(due);
        await db.SaveChangesAsync(cancellationToken);
    }
}

internal static class NotificationLinks
{
    /// <summary>Server-made links are relative paths; they are made absolute, anything that is not a path or http(s) is dropped.</summary>
    public static string? Absolute(string? link, string? publicAppUrl) => link switch
    {
        null or "" => null,
        _ when link.StartsWith('/') && !link.StartsWith("//", StringComparison.Ordinal) =>
            publicAppUrl is { Length: > 0 } root ? root.TrimEnd('/') + link : link,
        _ when Uri.TryCreate(link, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) => link,
        _ => null,
    };
}
