using Coworkee.Application;
using Coworkee.Application.Localization;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Coworkee.OData.Transfer;

namespace Coworkee.Mailing;

/// <summary>Mails export files in the owner's language, attached while they fit; larger files stay in the owner's job list.</summary>
internal sealed class ExportMailer(CoworkeeDbContext db, IMailSender mails, IUserDirectory users, ICurrentUser currentUser, IEnumerable<IDefaultLanguage> defaultLanguages)
    : IExportMailer
{
    public const string Template = "Data.Export";

    public async Task MailAsync(DataExportMail mail, CancellationToken cancellationToken)
    {
        var owner = (await users.GetActiveEmailsAsync([mail.OwnerId], cancellationToken)).GetValueOrDefault(mail.OwnerId);
        var name = (await users.GetDisplayNamesAsync([mail.OwnerId], cancellationToken)).GetValueOrDefault(mail.OwnerId);
        var culture = await RecipientCultures.OfAsync(db, mail.OwnerId, currentUser.TenantId, defaultLanguages, cancellationToken);
        var attached = mail.File.Content.Length <= MailAttachment.MaxTotalBytes;
        List<MailAttachment> attachments = attached ? [new MailAttachment(mail.File.Name, mail.File.ContentType, mail.File.Content)] : [];
        foreach (var to in new[] { owner }.Concat(mail.Recipients).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var model = new
            {
                user = new { first_name = to == owner ? name ?? to : to, email = to },
                export = new { name = mail.Name, file_name = mail.File.Name },
                attached,
            };
            await mails.QueueWithAttachmentsAsync(to, Template, model, culture.Name, attachments, cancellationToken);
        }
    }
}
