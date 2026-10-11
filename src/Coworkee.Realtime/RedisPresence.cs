using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Coworkee.Realtime;

/// <summary>
/// Who is online on which instance: one Redis hash per instance (user to organisation) that expires unless the instance keeps
/// writing it, so the users of a crashed instance drop out within <see cref="Ttl"/>.
/// </summary>
// ponytail: reads every instance's hash per query; a counter per organisation when there are dozens of instances
internal sealed partial class RedisPresence(IConnectionMultiplexer redis, Func<IReadOnlyList<KeyValuePair<Guid, Guid?>>> snapshot, ILogger logger)
{
    private const string Instances = "coworkee:presence:instances";
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    private readonly string _instance = Guid.NewGuid().ToString("N");

    private RedisKey Own => Key(_instance);

    /// <summary>Marks the user online here; true when another instance has them online already.</summary>
    public Task<bool> JoinAsync(Guid userId, Guid? tenantId) => SafeAsync(async database =>
    {
        await database.HashSetAsync(Own, userId.ToString("N"), Organisation(tenantId));
        await database.KeyExpireAsync(Own, Ttl);
        await database.SetAddAsync(Instances, _instance);
        return await OnlineElsewhereAsync(database, userId);
    }, false);

    /// <summary>Marks the user offline here; true when another instance still has them online.</summary>
    public Task<bool> LeaveAsync(Guid userId) => SafeAsync(async database =>
    {
        await database.HashDeleteAsync(Own, userId.ToString("N"));
        return await OnlineElsewhereAsync(database, userId);
    }, false);

    public Task<IReadOnlyList<Guid>> OnlineAsync(Guid? tenantId) => SafeAsync<IReadOnlyList<Guid>>(async database =>
    {
        var organisation = Organisation(tenantId);
        var online = new List<Guid>();
        foreach (var key in await OthersAsync(database))
        {
            online.AddRange((await database.HashGetAllAsync(key)).Where(e => e.Value == organisation).Select(e => Guid.Parse(e.Name.ToString())));
        }

        return online;
    }, []);

    /// <summary>Writes the users of this instance again and again: keeps the hash alive and restores it after a Redis restart.</summary>
    public async Task BeatAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(Ttl / 3);
        do
        {
            await SafeAsync(async database =>
            {
                var transaction = database.CreateTransaction();
                _ = transaction.KeyDeleteAsync(Own);
                _ = transaction.HashSetAsync(Own, [.. snapshot().Select(o => new HashEntry(o.Key.ToString("N"), Organisation(o.Value)))]);
                _ = transaction.KeyExpireAsync(Own, Ttl);
                _ = transaction.SetAddAsync(Instances, _instance);
                return await transaction.ExecuteAsync();
            }, false);
        }
        while (await WaitAsync(timer, cancellationToken));
    }

    public Task StopAsync() => SafeAsync(async database =>
    {
        await database.KeyDeleteAsync(Own);
        return await database.SetRemoveAsync(Instances, _instance);
    }, false);

    private static RedisKey Key(string instance) => "coworkee:presence:" + instance;

    private static string Organisation(Guid? tenantId) => tenantId?.ToString("N") ?? "host";

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task<bool> OnlineElsewhereAsync(IDatabase database, Guid userId)
    {
        foreach (var key in await OthersAsync(database))
        {
            if (await database.HashExistsAsync(key, userId.ToString("N")))
            {
                return true;
            }
        }

        return false;
    }

    // instances whose hash expired are gone
    private async Task<List<RedisKey>> OthersAsync(IDatabase database)
    {
        var others = new List<RedisKey>();
        foreach (var instance in (await database.SetMembersAsync(Instances)).Select(i => i.ToString()).Where(i => i != _instance))
        {
            if (await database.KeyExistsAsync(Key(instance)))
            {
                others.Add(Key(instance));
            }
            else
            {
                await database.SetRemoveAsync(Instances, instance);
            }
        }

        return others;
    }

    private async Task<T> SafeAsync<T>(Func<IDatabase, Task<T>> action, T fallback)
    {
        try
        {
            return await action(redis.GetDatabase());
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
            LogFailed(exception);
            return fallback;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sharing presence on Redis failed; users count per instance until it answers again")]
    private partial void LogFailed(Exception exception);
}
