namespace Coworkee.Application.Caching;

/// <summary>The defaults of cached queries that name no time of their own (the settings group "Cache" when Coworkee.Settings is installed).</summary>
public interface ICacheDefaults
{
    Task<CacheDefaults> GetAsync(CancellationToken cancellationToken);
}
