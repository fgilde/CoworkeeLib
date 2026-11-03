using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Mailing;

internal sealed class MailSender(CoworkeeDbContext db, IMailTemplateRenderer renderer, ICurrentUser currentUser, TimeProvider clock) : IMailSender
{
    public async Task<Guid> QueueAsync(string to, string template, object model, string? culture, Guid? campaignId, CancellationToken cancellationToken)
    {
        var rendered = await renderer.RenderAsync(template, model, culture, cancellationToken);
        var mail = OutgoingMail.Queue(to, rendered, template, currentUser.TenantId, clock.GetUtcNow(), campaignId);
        db.Add(mail);
        return mail.Id;
    }

    public async Task<Guid> QueueWithAttachmentsAsync(string to, string template, object model, string? culture, IReadOnlyList<MailAttachment> attachments, CancellationToken cancellationToken)
    {
        if (attachments.Sum(a => (long)a.Content.Length) > MailAttachment.MaxTotalBytes)
        {
            throw new ArgumentException($"The attachments of a mail are at most {MailAttachment.MaxTotalBytes / 1024 / 1024} MB together.", nameof(attachments));
        }

        var id = await QueueAsync(to, template, model, culture, null, cancellationToken);
        db.AddRange(attachments.Select(a => new OutgoingMailAttachment { MailId = id, FileName = a.FileName, ContentType = a.ContentType, Content = a.Content }));
        return id;
    }
}
