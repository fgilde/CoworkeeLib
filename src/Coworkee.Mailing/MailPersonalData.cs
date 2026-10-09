using Coworkee.Application.Privacy;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Mailing;

/// <summary>Mails queued for the user's address; the bodies stay out of the export, they only repeat what the application sent.</summary>
internal sealed class MailPersonalData(CoworkeeDbContext db) : IPersonalDataContributor
{
    public string Section => "mails";

    public async Task<object?> ExportAsync(PersonalDataSubject subject, CancellationToken cancellationToken) =>
        subject.Email is null ? null : await Mine(subject.Email).AsNoTracking().OrderBy(m => m.QueuedAt)
            .Select(m => new { m.To, m.Subject, m.TemplateName, m.Status, m.QueuedAt, m.SentAt })
            .ToListAsync(cancellationToken);

    public Task EraseAsync(PersonalDataSubject subject, CancellationToken cancellationToken) =>
        subject.Email is null ? Task.CompletedTask : Mine(subject.Email).ExecuteDeleteAsync(cancellationToken);

    private IQueryable<OutgoingMail> Mine(string email) => db.Set<OutgoingMail>().Where(m => m.To.ToLower() == email.ToLower());
}
