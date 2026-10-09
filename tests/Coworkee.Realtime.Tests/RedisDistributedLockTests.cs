using StackExchange.Redis;
using Testcontainers.Redis;

namespace Coworkee.Realtime.Tests;

public sealed class RedisDistributedLockTests : IAsyncLifetime
{
    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine").Build();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await _redis.StartAsync();

    public async ValueTask DisposeAsync() => await _redis.DisposeAsync();

    [Fact]
    public async Task Lock_is_exclusive_per_name_until_released()
    {
        await using var connection = await ConnectionMultiplexer.ConnectAsync(_redis.GetConnectionString());
        var locks = new RedisDistributedLock(connection);

        var first = await locks.AcquireAsync("digest", TimeSpan.Zero, Ct);
        first.ShouldNotBeNull();
        (await locks.AcquireAsync("digest", TimeSpan.FromMilliseconds(300), Ct)).ShouldBeNull();

        var waiting = locks.AcquireAsync("digest", TimeSpan.FromSeconds(10), Ct);
        await Task.Delay(300, Ct);
        waiting.IsCompleted.ShouldBeFalse();
        await first.DisposeAsync();

        await using var second = await waiting;
        second.ShouldNotBeNull();
    }
}
