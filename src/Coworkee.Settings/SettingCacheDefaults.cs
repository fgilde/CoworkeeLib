using Coworkee.Application.Caching;
using Coworkee.Contracts.Settings;

namespace Coworkee.Settings;

/// <summary>The defaults of cached queries from the settings group "Cache" (<see cref="CacheSettings"/>); 0 minutes means none.</summary>
internal sealed class SettingCacheDefaults(ISettingProvider settings) : ICacheDefaults
{
    public async Task<CacheDefaults> GetAsync(CancellationToken cancellationToken) => new(
        Minutes(await settings.GetAsync<int?>(CacheSettings.DurationMinutes, cancellationToken)),
        Minutes(await settings.GetAsync<int?>(CacheSettings.RefreshAfterMinutes, cancellationToken)),
        await settings.GetAsync<bool?>(CacheSettings.RefreshInBackground, cancellationToken) ?? true);

    private static TimeSpan? Minutes(int? minutes) => minutes > 0 ? TimeSpan.FromMinutes(minutes.Value) : null;
}
