using Coworkee.Application.Retention;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Ai.Sessions;

internal sealed class AiSessionRetention(CoworkeeDbContext db) : IRetentionTarget
{
    public string Key => "ai-sessions";

    public string Title => "Assistant conversations";

    public string? Description => "Conversations with the assistant, by their last message, with all their messages.";

    public int? DefaultDays => null;

    public Task<long> CountAsync(DateTimeOffset before, CancellationToken cancellationToken) => Older(before).LongCountAsync(cancellationToken);

    public Task<long> DeleteAsync(DateTimeOffset before, CancellationToken cancellationToken) =>
        Older(before).OrderBy(s => s.UpdatedAt).ExecuteDeleteInBatchesAsync(cancellationToken);

    private IQueryable<AiChatSession> Older(DateTimeOffset before) => db.Set<AiChatSession>().IgnoreQueryFilters().Where(s => s.UpdatedAt < before);
}
