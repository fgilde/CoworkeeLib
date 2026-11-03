namespace Coworkee.Application.Caching;

/// <summary>A cached value with the time it was loaded, for entries refreshed in the background (<see cref="ICachedQuery.CacheRefreshAfter"/>).</summary>
internal sealed record CacheEntry<T>(T Value, DateTimeOffset LoadedUtc);
