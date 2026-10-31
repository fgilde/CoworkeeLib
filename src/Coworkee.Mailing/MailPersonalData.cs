using Coworkee.Application.Privacy;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Mailing;

/// <summary>
/// Mails queued for the user's address in his organisation; the bodies stay out of the export, they only repeat what the application sent. The same address
/// may belong to another user of another organisation, whose mails stay.
/// </summary>
internal sealed class MailPersonalData(CoworkeeDbContext db) : IPersonalDataContributor
{
    public string Section => "mails";

    public async Task<object?> ExportAsync(PersonalDataSubject subject, CancellationToken cancellationToken) =>
        subject.Email is null ? null : await Mine(subject).AsNoTracking().OrderBy(m => m.QueuedAt)
            .Select(m => new { m.To, m.Subject, m.TemplateName, m.Status, m.QueuedAt, m.SentAt })
            .ToListAsync(cancellationToken);

    public Task EraseAsync(PersonalDataSubject subject, CancellationToken cancellationToken) =>
        subject.Email is null ? Task.CompletedTask : Mine(subject).ExecuteDeleteAsync(cancellationToken);

    private IQueryable<OutgoingMail> Mine(PersonalDataSubject subject)
    {
        var email = subject.Email!.ToLower();
        return db.Set<OutgoingMail>().Where(m => m.TenantId == subject.TenantId && m.To.ToLower() == email);
    }
}
