using Coworkee.Application;
using Coworkee.Application.Localization;
using Coworkee.Application.Messaging;
using Coworkee.BackgroundJobs;
using Coworkee.Contracts.Configuration;
using Coworkee.Core.Security;
using Coworkee.Domain;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Mailing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Scriban.Runtime;
using System.Text.Json;

namespace Coworkee.Notifications;

/// <summary>A notification mail that waits for its type's delay; a newer one of the same user, type and entity replaces it.</summary>
[NotAudited]
public sealed class PendingNotificationMail : Entity
{
    public Guid UserId { get; set; }

    public Guid? TenantId { get; set; }

    public required string Type { get; set; }

    /// <summary>The entity ("Shipments:{id}") that debounces; a mail without an entity has a key of its own. Unique per user and type.</summary>
    public required string Key { get; set; }

    public required string Title { get; set; }

    public string? Body { get; set; }

    public List<string>? Arguments { get; set; }

    public string? Link { get; set; }

    /// <summary>The <see cref="NotificationMessage.MailModel"/> as JSON.</summary>
    public string? MailModel { get; set; }

    public DateTimeOffset DueAt { get; set; }

    /// <summary>New on every change: the mail job sends exactly the version it read.</summary>
    public Guid Version { get; set; }
}

/// <summary>A notification mail to wait, written after the command that sent it committed.</summary>
public sealed record NotificationMailWaiting(
    Guid UserId, Guid? TenantId, string Type, string Key, string Title, string? Body, IReadOnlyList<string>? Arguments, string? Link, DateTimeOffset DueAt,
    string? MailModel = null) : IDomainEvent;

/// <summary>Adds the waiting mail or replaces the one of the same user, type and entity (debounce); each try in a context of its own, the outbox retries.</summary>
internal sealed partial class NotificationMailWaitingHandler(IServiceScopeFactory scopes, ILogger<NotificationMailWaitingHandler> logger) : IDomainEventHandler<NotificationMailWaiting>
{
    private const int Attempts = 3;

    public async Task HandleAsync(NotificationMailWaiting waiting, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CoworkeeDbContext>();
            var mail = await db.Set<PendingNotificationMail>()
                .FirstOrDefaultAsync(m => m.UserId == waiting.UserId && m.Type == waiting.Type && m.Key == waiting.Key, cancellationToken);
            if (mail is null)
            {
                mail = new PendingNotificationMail { UserId = waiting.UserId, TenantId = waiting.TenantId, Type = waiting.Type, Key = waiting.Key, Title = waiting.Title };
                db.Add(mail);
            }
            else if (waiting.DueAt < mail.DueAt)
            {
                // an older notification, retried by the outbox after a newer one: the newer text stays
                return;
            }

            (mail.Title, mail.Body, mail.Link, mail.MailModel, mail.DueAt, mail.Version) = (waiting.Title, waiting.Body, waiting.Link, waiting.MailModel, waiting.DueAt, Guid.NewGuid());
            mail.Arguments = waiting.Arguments is null ? null : [.. waiting.Arguments];
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateException exception) when (attempt < Attempts)
            {
                // another notification added it, or the mail job took it meanwhile: look again
                LogRetry(exception, waiting.Type, waiting.UserId);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "The waiting mail of {Type} for {UserId} changed meanwhile; trying again.")]
    private partial void LogRetry(Exception exception, string type, Guid userId);
}

/// <summary>
/// Writes one notification as mail in the reader's language, with its type's template or <see cref="Template"/>; the mail joins the current unit of work.
/// </summary>
internal sealed partial class NotificationMails(
    CoworkeeDbContext db, IMailSender mails, IUserDirectory users, IEnumerable<ITextTranslator> translators, IOptions<NotificationOptions> options,
    NotificationTypeOptions types, IMailTemplateCatalog catalog, ILogger<NotificationMails> logger)
{
    public const string Template = "Notifications.Notification";

    public sealed record Item(Guid UserId, Guid? TenantId, string Type, string Title, string? Body, IReadOnlyList<string>? Arguments, string? Link, string? MailModel);

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
            var model = NotificationMailModel.Build(
                item.MailModel,
                new { first_name = names.GetValueOrDefault(item.UserId) ?? email, email },
                new
                {
                    title = NotificationText.In(item.Title, item.Arguments, culture, translators),
                    body = NotificationText.In(item.Body, item.Arguments, culture, translators),
                    link = NotificationLinks.Absolute(item.Link, options.Value.PublicAppUrl),
                });
            await QueueOneAsync(email, await TemplateAsync(item.Type, cancellationToken), model, culture?.Name, item, cancellationToken);
        }
    }

    // a template that fails to render (an edited version, a model it cannot read) falls back to the generic mail, and when that fails too the mail is
    // dropped: neither stops the other mails of the job's batch nor the sending command. The sender renders before it adds the mail, so nothing is left behind.
    private async Task QueueOneAsync(string email, string template, object model, string? culture, Item item, CancellationToken cancellationToken)
    {
        try
        {
            await mails.QueueAsync(email, template, model, culture, cancellationToken);
            return;
        }
        catch (Exception exception) when (exception is not OperationCanceledException && template != Template)
        {
            LogRenderFailed(exception, template, item.Type, item.UserId);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogDropped(exception, item.Type, item.UserId);
            return;
        }

        try
        {
            await mails.QueueAsync(email, Template, model, culture, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogDropped(exception, item.Type, item.UserId);
        }
    }

    // a template that does not exist (any more) must not stop the mails: they go out generic
    private async Task<string> TemplateAsync(string type, CancellationToken cancellationToken)
    {
        if (types.Find(type)?.MailTemplate is not { } template)
        {
            return Template;
        }

        if (await catalog.FindAsync(template, cancellationToken) is not null)
        {
            return template;
        }

        LogMissingTemplate(template, type);
        return Template;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The mail template {Template} of the notification type {Type} does not exist; the generic notification mail goes out.")]
    private partial void LogMissingTemplate(string template, string type);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The mail template {Template} of the notification type {Type} failed for {UserId}; the generic notification mail goes out.")]
    private partial void LogRenderFailed(Exception exception, string template, string type, Guid userId);

    [LoggerMessage(Level = LogLevel.Error, Message = "The notification mail of {Type} for {UserId} could not be rendered and is dropped.")]
    private partial void LogDropped(Exception exception, string type, Guid userId);
}

/// <summary>The model of a notification mail: the members of <see cref="NotificationMessage.MailModel"/> next to <c>user</c> and <c>notification</c>.</summary>
public static class NotificationMailModel
{
    /// <summary>The largest model, as UTF-8 JSON.</summary>
    public const int MaxBytes = 32 * 1024;

    // names the library fills (user, notification, the layout's subject, content and unsubscribe_url) and Scriban's builtin objects; compared without case and underscores
    private static readonly HashSet<string> Reserved = new(
        new[] { "user", "notification", "subject", "content", "unsubscribe_url" }.Concat(new Scriban.Functions.BuiltinFunctions().Keys).Select(Normalize),
        StringComparer.Ordinal);
    private static readonly object SampleUser = new { first_name = "Ada", email = "ada@example.com" };

    private static readonly object SampleNotification = new { title = "Document shared", body = "Grace shared a document with you.", link = "https://example.com/d/1" };

    /// <summary>
    /// The sample of a notification type's mail template (<see cref="MailTemplateDefinition.SampleModel"/>): <paramref name="model"/> with a sample
    /// <c>user</c> and <c>notification</c>, so the preview and the test mail on the mail templates page show what a real mail shows.
    /// </summary>
    public static object Sample(object model)
    {
        var sample = new ScriptObject();
        foreach (var property in model.GetType().GetProperties().Where(p => p.GetIndexParameters().Length == 0))
        {
            sample[StandardMemberRenamer.Default(property)] = property.GetValue(model);
        }

        sample["user"] = SampleUser;
        sample["notification"] = SampleNotification;
        return sample;
    }

    /// <summary>The model as JSON for the waiting mail: an object (or a dictionary) of at most <see cref="MaxBytes"/> without reserved top-level names.</summary>
    internal static string? Json(object? model)
    {
        if (model is null)
        {
            return null;
        }

        var json = JsonSerializer.SerializeToElement(model);
        if (json.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("The mail model of a notification is an object or a dictionary.", nameof(model));
        }

        if (json.EnumerateObject().Select(p => p.Name).FirstOrDefault(IsReserved) is { } reserved)
        {
            throw new ArgumentException($"The mail model of a notification cannot have a member '{reserved}': the library fills it.", nameof(model));
        }

        var text = json.GetRawText();
        return System.Text.Encoding.UTF8.GetByteCount(text) <= MaxBytes
            ? text
            : throw new ArgumentException($"The mail model of a notification is at most {MaxBytes / 1024} KB as JSON.", nameof(model));
    }

    /// <summary>The model's members next to the trusted <c>user</c> and <c>notification</c>, which always win; reserved names of older rows are left out.</summary>
    internal static Dictionary<string, object?> Build(string? json, object user, object notification)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (json is not null)
        {
            foreach (var (name, value) in JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!)
            {
                if (!IsReserved(name))
                {
                    values[StandardMemberRenamer.Rename(name)] = value;
                }
            }
        }

        values["user"] = user;
        values["notification"] = notification;
        return values;
    }

    private static bool IsReserved(string name) => Reserved.Contains(Normalize(name));

    private static string Normalize(string name) => name.Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
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
        var due = await db.Set<PendingNotificationMail>().AsNoTracking().Where(m => m.DueAt <= now).OrderBy(m => m.DueAt).Take(Batch).ToListAsync(cancellationToken);
        if (due.Count == 0)
        {
            return;
        }

        // the claims and the queued mails commit together: a mail is neither lost nor sent twice
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var claimed = new List<PendingNotificationMail>();
        foreach (var mail in due)
        {
            if (await ClaimAsync(mail, cancellationToken))
            {
                claimed.Add(mail);
            }
        }

        // ponytail: one mail per notification through the global mail settings, as the digest; templates and the mail log follow the mail's tenant
        foreach (var tenant in claimed.GroupBy(m => m.TenantId))
        {
            using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, tenant.Key));
            await mails.QueueAsync([.. tenant.Select(m => new NotificationMails.Item(m.UserId, m.TenantId, m.Type, m.Title, m.Body, m.Arguments, m.Link, m.MailModel))], cancellationToken);
        }

        // the MailQueued outbox rows of these mails get the tenant of this save, which is none: harmless today, because their handler only enqueues the send
        // job by mail id, and that job reads the mail by id and sends through the global mail settings
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Takes the mail as it was read; false when a publisher replaced it meanwhile (it waits anew) or another run took it.</summary>
    internal async Task<bool> ClaimAsync(PendingNotificationMail seen, CancellationToken cancellationToken) =>
        await db.Set<PendingNotificationMail>().Where(m => m.Id == seen.Id && m.Version == seen.Version).ExecuteDeleteAsync(cancellationToken) == 1;
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
