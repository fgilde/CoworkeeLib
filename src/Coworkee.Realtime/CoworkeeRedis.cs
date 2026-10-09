using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace Coworkee.Realtime;

/// <summary>
/// With a <c>redis</c> connection string the services share one Redis connection, and HybridCache gets Redis as distributed second
/// level for the entries that ask for it (<see cref="Shared"/>). The others stay per instance: removing by tag does not reach the
/// second level other instances read, so tag-invalidated caches would serve stale values from it.
/// </summary>
public static class CoworkeeRedis
{
    public const string ConnectionStringName = "redis";

    /// <summary>Entry flags that use the distributed level when there is one.</summary>
    public const HybridCacheEntryFlags Shared = HybridCacheEntryFlags.None;

    public static string? ConnectionString(IConfiguration configuration) =>
        configuration.GetConnectionString(ConnectionStringName) is { Length: > 0 } redis ? redis : null;

    public static IServiceCollection AddCoworkeeRedis(this IServiceCollection services, IConfiguration configuration)
    {
        if (ConnectionString(configuration) is not { } redis)
        {
            return services;
        }

        services.TryAddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redis));
        if (!services.Any(d => d.ServiceType == typeof(IDistributedCache)))
        {
            // a cache that does not answer quickly is skipped: the entries load from the database instead
            var cache = ConfigurationOptions.Parse(redis);
            cache.AbortOnConnectFail = false;
            cache.AsyncTimeout = cache.SyncTimeout = 500;
            cache.ConnectTimeout = 1000;
            services.AddStackExchangeRedisCache(options =>
            {
                options.ConfigurationOptions = cache;
                options.InstanceName = "coworkee:cache:";
            });
            services.Configure<HybridCacheOptions>(options => options.DefaultEntryOptions = new HybridCacheEntryOptions
            {
                Expiration = options.DefaultEntryOptions?.Expiration,
                LocalCacheExpiration = options.DefaultEntryOptions?.LocalCacheExpiration,
                Flags = (options.DefaultEntryOptions?.Flags ?? HybridCacheEntryFlags.None) | HybridCacheEntryFlags.DisableDistributedCache,
            });
        }

        return services;
    }
}
