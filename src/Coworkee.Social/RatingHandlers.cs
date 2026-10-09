using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Social;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Social;

internal sealed class RatingHandlers(CoworkeeDbContext db, SocialGuard guard, IPermissionChecker permissions, ICurrentUser currentUser)
    : IHandler<GetRatingQuery, Result<RatingDto>>,
      IHandler<RateCommand, Result<RatingDto>>,
      IHandler<ClearRatingCommand, Result<RatingDto>>
{
    private Guid Me => currentUser.UserId ?? throw new InvalidOperationException("Ratings need a signed-in user.");

    public async Task<Result<RatingDto>> HandleAsync(GetRatingQuery query, CancellationToken cancellationToken) =>
        await guard.CheckAsync(Access(query.EntityType, query.EntityId, write: false), cancellationToken) is { } error
            ? error
            : await LoadAsync(query.EntityType, query.EntityId, cancellationToken);

    public async Task<Result<RatingDto>> HandleAsync(RateCommand command, CancellationToken cancellationToken)
    {
        if (await guard.CheckAsync(Access(command.EntityType, command.EntityId, write: true), cancellationToken) is { } error)
        {
            return error;
        }

        var me = Me;
        var rating = await db.Set<Rating>()
            .SingleOrDefaultAsync(r => r.EntityType == command.EntityType && r.EntityId == command.EntityId && r.UserId == me, cancellationToken);
        if (rating is null)
        {
            rating = new Rating { TenantId = currentUser.TenantId!.Value, EntityType = command.EntityType, EntityId = command.EntityId, UserId = me };
            db.Add(rating);
        }

        rating.Stars = command.Stars;
        await db.SaveChangesAsync(cancellationToken);
        return await LoadAsync(command.EntityType, command.EntityId, cancellationToken);
    }

    public async Task<Result<RatingDto>> HandleAsync(ClearRatingCommand command, CancellationToken cancellationToken)
    {
        var userId = command.UserId ?? Me;
        if (userId != Me && !await permissions.IsGrantedAsync(SocialPermissions.Ratings.Moderate, cancellationToken))
        {
            return SocialGuard.Forbidden;
        }

        if (await guard.CheckAsync(Access(command.EntityType, command.EntityId, write: true), cancellationToken) is { } error)
        {
            return error;
        }

        await Of(command.EntityType, command.EntityId).Where(r => r.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        return await LoadAsync(command.EntityType, command.EntityId, cancellationToken);
    }

    private async Task<RatingDto> LoadAsync(string entityType, Guid entityId, CancellationToken cancellationToken)
    {
        var me = Me;
        var stats = await Of(entityType, entityId)
            .GroupBy(_ => 1)
            .Select(g => new { Average = g.Average(r => (double)r.Stars), Count = g.Count(), Mine = g.Where(r => r.UserId == me).Select(r => (int?)r.Stars).FirstOrDefault() })
            .SingleOrDefaultAsync(cancellationToken);
        var canRate = await permissions.IsGrantedAsync(SocialPermissions.Ratings.Create, cancellationToken)
            && await guard.AllowsAsync(Access(entityType, entityId, write: true), cancellationToken);
        return new RatingDto(Math.Round(stats?.Average ?? 0, 2), stats?.Count ?? 0, stats?.Mine, canRate);
    }

    private IQueryable<Rating> Of(string entityType, Guid entityId) =>
        db.Set<Rating>().AsNoTracking().Where(r => r.EntityType == entityType && r.EntityId == entityId);

    private static SocialAccess Access(string entityType, Guid entityId, bool write) => new(SocialFeature.Ratings, entityType, entityId, write);
}
