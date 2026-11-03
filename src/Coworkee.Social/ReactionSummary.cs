using Coworkee.Contracts.Social;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Social;

internal static class ReactionSummary
{
    // ponytail: every user id of every emoji; a cap with "and n more" when entities collect thousands of reactions
    public static async Task<Dictionary<Guid, IReadOnlyList<ReactionDto>>> OfAsync(CoworkeeDbContext db, string entityType, IReadOnlyCollection<Guid> entityIds, CancellationToken cancellationToken)
    {
        var rows = await db.Set<Reaction>().AsNoTracking().Where(r => r.EntityType == entityType && entityIds.Contains(r.EntityId))
            .Select(r => new { r.EntityId, r.Emoji, r.UserId, r.CreatedAt }).ToListAsync(cancellationToken);
        return rows.GroupBy(r => r.EntityId).ToDictionary(
            e => e.Key,
            e => (IReadOnlyList<ReactionDto>)[.. e.GroupBy(r => r.Emoji).OrderBy(g => g.Min(r => r.CreatedAt))
                .Select(g => new ReactionDto(g.Key, [.. g.OrderBy(r => r.CreatedAt).Select(r => r.UserId)]))]);
    }
}
