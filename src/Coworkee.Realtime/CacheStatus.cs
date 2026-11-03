using System.Diagnostics;
using Coworkee.Application.Status;
using Coworkee.Contracts.Settings;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Coworkee.Realtime;

internal sealed class CacheStatus(IServiceProvider services) : ISystemStatusContributor
{
    public async Task<SystemStatusSection> GetAsync(CancellationToken cancellationToken)
    {
        if (services.GetService<IConnectionMultiplexer>() is not { } redis)
        {
            return new SystemStatusSection("cache", "Cache", [SystemStatusEntry.Text("Redis", "Not configured: every instance caches on its own")], Order: 5);
        }

        if (!redis.IsConnected)
        {
            return new SystemStatusSection("cache", "Cache", [SystemStatusEntry.Text("Redis", "Disconnected", SystemStatusLevel.Error)], Order: 5);
        }

        var watch = Stopwatch.StartNew();
        await redis.GetDatabase().PingAsync();
        return new SystemStatusSection("cache", "Cache",
        [
            SystemStatusEntry.Text("Redis", "Connected", SystemStatusLevel.Ok),
            new("Latency", $"{watch.Elapsed.TotalMilliseconds:0} ms"),
        ], Order: 5);
    }
}
