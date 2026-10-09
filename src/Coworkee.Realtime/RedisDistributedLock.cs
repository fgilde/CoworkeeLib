using System.Diagnostics;
using Coworkee.Application;
using StackExchange.Redis;

namespace Coworkee.Realtime;

/// <summary>Redis lock with a short expiry that the holder keeps extending, so a crashed holder frees it within <see cref="Expiry"/>.</summary>
internal sealed class RedisDistributedLock(IConnectionMultiplexer redis) : IDistributedLock
{
    public const string ProviderSetting = "Coworkee:DistributedLock:Provider";

    private static readonly TimeSpan Expiry = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    public async Task<IAsyncDisposable?> AcquireAsync(string name, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var database = redis.GetDatabase();
        var key = new RedisKey("coworkee:lock:" + name);
        var token = new RedisValue(Guid.NewGuid().ToString("N"));
        var started = Stopwatch.GetTimestamp();
        while (!await database.LockTakeAsync(key, token, Expiry))
        {
            if (Stopwatch.GetElapsedTime(started) >= timeout)
            {
                return null;
            }

            await Task.Delay(PollInterval, cancellationToken);
        }

        return new Handle(database, key, token);
    }

    private sealed class Handle : IAsyncDisposable
    {
        private readonly IDatabase _database;
        private readonly RedisKey _key;
        private readonly RedisValue _token;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _renewal;

        public Handle(IDatabase database, RedisKey key, RedisValue token)
        {
            (_database, _key, _token) = (database, key, token);
            _renewal = RenewAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            await _renewal;
            _stop.Dispose();
            await _database.LockReleaseAsync(_key, _token);
        }

        private async Task RenewAsync()
        {
            using var timer = new PeriodicTimer(Expiry / 3);
            try
            {
                while (await timer.WaitForNextTickAsync(_stop.Token))
                {
                    await _database.LockExtendAsync(_key, _token, Expiry);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
