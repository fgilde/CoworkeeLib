using Coworkee.Infrastructure.Persistence;

namespace Coworkee.Social;

/// <param name="VisibleIdsAsync">The ids the current user sees, as a key read (a guard of one id) or as a list (bool false); null when he may not see the type at all.</param>
/// <param name="ClrType">The entity type of a typed registration; its client entity copies hear of the writes.</param>
internal sealed record SocialTarget(
    Func<IServiceProvider, bool, CancellationToken, Task<IQueryable<Guid>?>>? VisibleIdsAsync,
    Func<CoworkeeDbContext, Guid, CancellationToken, Task<Guid?>>? OwnerAsync,
    Func<Guid, string>? Link,
    Type? ClrType = null);
