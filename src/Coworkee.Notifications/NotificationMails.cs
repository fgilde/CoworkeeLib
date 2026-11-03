using Coworkee.Application.Localization;
using Coworkee.Contracts.Configuration;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Mailing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Coworkee.Notifications;

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
            // without a model (e.g. queued before its type got a template) the type's template would render empty: the generic mail goes out
            var template = item.MailModel is null ? Template : await TemplateAsync(item.Type, cancellationToken);
            await QueueOneAsync(email, template, model, culture?.Name, item, cancellationToken);
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
