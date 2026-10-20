using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Social;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Social;

internal sealed class ReactionHandlers(CoworkeeDbContext db, SocialGuard guard, IPermissionChecker permissions, ICurrentUser currentUser, TimeProvider clock)
    : IHandler<GetReactionsQuery, Result<ReactionsDto>>,
      IHandler<ToggleReactionCommand, Result<ReactionsDto>>
{
    private static readonly Error TooMany = Error.Conflict("reactions.too_many", "You have reacted too often on this.");

    private Guid Me => currentUser.UserId ?? throw new InvalidOperationException("Reactions need a signed-in user.");

    public async Task<Result<ReactionsDto>> HandleAsync(GetReactionsQuery query, CancellationToken cancellationToken) =>
        await guard.CheckAsync(Access(query.EntityType, query.EntityId, write: false), cancellationToken) is { } error
            ? error
            : await LoadAsync(query.EntityType, query.EntityId, cancellationToken);

    public async Task<Result<ReactionsDto>> HandleAsync(ToggleReactionCommand command, CancellationToken cancellationToken)
    {
        if (await guard.CheckAsync(Access(command.EntityType, command.EntityId, write: true), cancellationToken) is { } error)
        {
            return error;
        }

        var me = Me;
        var mine = await db.Set<Reaction>().Where(r => r.EntityType == command.EntityType && r.EntityId == command.EntityId && r.UserId == me).ToListAsync(cancellationToken);
        var threadTopic = command.EntityType == SocialReactions.Comment && await guard.CommentOfAsync(command.EntityId, cancellationToken) is { } comment
            ? SocialTopics.Comments(comment.EntityType, comment.EntityId)
            : null;
        if (mine.FirstOrDefault(r => r.Emoji == command.Emoji) is { } existing)
        {
            existing.ThreadTopic = threadTopic;
            db.Remove(existing);
        }
        else if (mine.Count >= SocialLimits.ReactionsPerUser)
        {
            return TooMany;
        }
        else
        {
            db.Add(new Reaction
            {
                TenantId = currentUser.TenantId!.Value, EntityType = command.EntityType, EntityId = command.EntityId, UserId = me, Emoji = command.Emoji,
                CreatedAt = clock.GetUtcNow(), ThreadTopic = threadTopic,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        return await LoadAsync(command.EntityType, command.EntityId, cancellationToken);
    }

    private async Task<ReactionsDto> LoadAsync(string entityType, Guid entityId, CancellationToken cancellationToken)
    {
        var reactions = await ReactionSummary.OfAsync(db, entityType, [entityId], cancellationToken);
        var canReact = await permissions.IsGrantedAsync(SocialPermissions.Reactions.Create, cancellationToken)
            && await guard.AllowsAsync(Access(entityType, entityId, write: true), cancellationToken);
        return new ReactionsDto(reactions.GetValueOrDefault(entityId, []), canReact);
    }

    private static SocialAccess Access(string entityType, Guid entityId, bool write) => new(SocialFeature.Reactions, entityType, entityId, write);
}

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
