namespace Coworkee.Application.Caching;

public interface ICachedQuery
{
    string CacheKey { get; }

    CacheScope CacheScope => CacheScope.Tenant;

    TimeSpan? CacheDuration => null;

    IReadOnlyList<string> CacheTags => [];
}
