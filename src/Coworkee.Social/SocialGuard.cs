using Coworkee.Application.Authorization;
using Coworkee.Contracts.Social;
using Coworkee.Core.Results;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Social;

/// <summary>Checks that the entity type is opted in, the current user sees the entity (through its OData set, if it has one) and the app's access checks pass.</summary>
internal sealed class SocialGuard(CoworkeeDbContext db, SocialOptions options, IPermissionChecker permissions, IServiceProvider services)
{
    public static readonly Error NotFound = Error.NotFound("social.not_found", "There is no such entity.");

    public static readonly Error Forbidden = Error.Forbidden("social.forbidden", "You may not do this on this entity.");

    public async Task<Error?> CheckAsync(SocialAccess access, CancellationToken cancellationToken)
    {
        if (!options.Targets.TryGetValue((access.Feature, access.EntityType), out var target))
        {
            return NotFound;
        }

        if (target.VisibleIdsAsync is { } visibleIds)
        {
            var ids = await visibleIds(services, cancellationToken);
            if (access.EntityId is { } id ? ids is null || !await ids.AnyAsync(x => x == id, cancellationToken) : ids is null)
            {
                return access.EntityId is null ? Forbidden : NotFound;
            }
        }

        if (access is { Feature: SocialFeature.Reactions, EntityType: SocialReactions.Comment })
        {
            return await CommentOfAsync(access.EntityId, cancellationToken) is not { } comment
                ? NotFound
                : await permissions.IsGrantedAsync(SocialPermissions.Comments.View, cancellationToken)
                    ? await CheckAsync(new SocialAccess(SocialFeature.Comments, comment.EntityType, comment.EntityId, access.Write), cancellationToken)
                    : Forbidden;
        }

        foreach (var check in options.AccessChecks)
        {
            if (!await check(services, access, cancellationToken))
            {
                return Forbidden;
            }
        }

        return null;
    }

    public async Task<bool> AllowsAsync(SocialAccess access, CancellationToken cancellationToken) => await CheckAsync(access, cancellationToken) is null;

    /// <summary>The ids of <paramref name="entityType"/> the current user sees; null when the type checks no ids.</summary>
    public async Task<IQueryable<Guid>?> VisibleIdsAsync(SocialFeature feature, string entityType, CancellationToken cancellationToken) =>
        options.Targets[(feature, entityType)].VisibleIdsAsync is { } visibleIds ? await visibleIds(services, cancellationToken) : null;

    public SocialTarget Target(SocialFeature feature, string entityType) => options.Targets[(feature, entityType)];

    public bool HasCommentReactions => options.Targets.ContainsKey((SocialFeature.Reactions, SocialReactions.Comment));

    /// <summary>The entity a comment belongs to, for reactions on it.</summary>
    public async Task<(string EntityType, Guid EntityId)?> CommentOfAsync(Guid? commentId, CancellationToken cancellationToken) =>
        await db.Set<Comment>().AsNoTracking().Where(c => c.Id == commentId).Select(c => new { c.EntityType, c.EntityId }).SingleOrDefaultAsync(cancellationToken) is { } comment
            ? (comment.EntityType, comment.EntityId)
            : null;
}
