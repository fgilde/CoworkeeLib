using Coworkee.Core.Results;
using Coworkee.Infrastructure.Persistence;

namespace Coworkee.Social;

/// <summary>Checks that the entity type is opted in, the entity exists in the own organisation and the app's access checks pass.</summary>
internal sealed class SocialGuard(CoworkeeDbContext db, SocialOptions options, IServiceProvider services)
{
    public static readonly Error NotFound = Error.NotFound("social.not_found", "There is no such entity.");

    public static readonly Error Forbidden = Error.Forbidden("social.forbidden", "You may not do this on this entity.");

    public async Task<Error?> CheckAsync(SocialAccess access, CancellationToken cancellationToken)
    {
        if (!options.Targets.TryGetValue((access.Feature, access.EntityType), out var target))
        {
            return NotFound;
        }

        if (access.EntityId is { } id && target.ExistsAsync is { } exists && !await exists(db, id, cancellationToken))
        {
            return NotFound;
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

    public SocialTarget Target(SocialFeature feature, string entityType) => options.Targets[(feature, entityType)];
}
