namespace Coworkee.Application.Caching;

public interface ICachedQuery
{
    string CacheKey { get; }

    CacheScope CacheScope => CacheScope.Tenant;

    TimeSpan? CacheDuration => null;

    /// <summary>
    /// Stale while revalidate: an entry older than this answers at once and is loaded again in the background (one load across the instances);
    /// null for the default of <see cref="ICacheDefaults"/>.
    /// </summary>
    TimeSpan? CacheRefreshAfter => null;

    /// <summary>One entry per selection of active groups (<see cref="Groups.IGroupScope.ActiveAsync"/>), for queries whose rows follow the groups.</summary>
    bool CacheByGroups => false;

    IReadOnlyList<string> CacheTags => [];
}
