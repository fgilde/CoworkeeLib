namespace Coworkee.Application.Caching;

/// <param name="Duration">How long an entry lives; null for the cache's own default (5 minutes).</param>
/// <param name="RefreshAfter">Age after which an entry answers at once and is loaded again in the background; null for none.</param>
/// <param name="RefreshInBackground">False turns background refreshing off, also for queries that name <see cref="ICachedQuery.CacheRefreshAfter"/>.</param>
public sealed record CacheDefaults(TimeSpan? Duration, TimeSpan? RefreshAfter, bool RefreshInBackground);
