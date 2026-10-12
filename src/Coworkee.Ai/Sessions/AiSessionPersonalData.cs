using Coworkee.Application.Privacy;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Ai.Sessions;

internal sealed class AiSessionPersonalData(CoworkeeDbContext db) : IPersonalDataContributor
{
    public string Section => "aiConversations";

    public async Task<object?> ExportAsync(PersonalDataSubject subject, CancellationToken cancellationToken) =>
        await Mine(subject).AsNoTracking().OrderBy(s => s.CreatedAt)
            .Select(s => new { s.Title, s.CreatedAt, Messages = s.Messages.OrderBy(m => m.At).Select(m => new { m.Role, m.Text, m.At }) })
            .ToListAsync(cancellationToken);

    public async Task EraseAsync(PersonalDataSubject subject, CancellationToken cancellationToken)
    {
        await db.Set<AiChatMessage>().Where(m => Mine(subject).Select(s => s.Id).Contains(m.SessionId)).ExecuteDeleteAsync(cancellationToken);
        await Mine(subject).ExecuteDeleteAsync(cancellationToken);
    }

    private IQueryable<AiChatSession> Mine(PersonalDataSubject subject) => db.Set<AiChatSession>().Where(s => s.UserId == subject.UserId);
}
