using Coworkee.Application.Privacy;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Chat;

/// <summary>Sent and received direct messages; erasing removes both sides of the user's conversations.</summary>
internal sealed class ChatPersonalData(CoworkeeDbContext db) : IPersonalDataContributor
{
    public string Section => "chat";

    public async Task<object?> ExportAsync(PersonalDataSubject subject, CancellationToken cancellationToken) =>
        await Mine(subject).AsNoTracking().OrderBy(m => m.SentAt)
            .Select(m => new { m.FromUserId, m.ToUserId, m.Text, m.SentAt, m.ReadAt })
            .ToListAsync(cancellationToken);

    public Task EraseAsync(PersonalDataSubject subject, CancellationToken cancellationToken) => Mine(subject).ExecuteDeleteAsync(cancellationToken);

    private IQueryable<ChatMessage> Mine(PersonalDataSubject subject) =>
        db.Set<ChatMessage>().IgnoreQueryFilters().Where(m => m.FromUserId == subject.UserId || m.ToUserId == subject.UserId);
}
