using Coworkee.Contracts.Realtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Coworkee.Realtime;

/// <summary>
/// Users with at least one hub connection are online; the last connection gone, they turn offline after <see cref="Grace"/>, so a
/// reload or a reconnect does not flicker. Every change goes to the organisation on <see cref="PresenceEvents.Topic"/>. With Redis the
/// instances share who is online (<see cref="RedisPresence"/>): a user counts once, however many instances hold connections.
/// </summary>
public sealed class RealtimePresence(IRealtimePublisher publisher, IServiceProvider services, ILogger<RealtimePresence> logger) : IHostedService
{
    private readonly Dictionary<Guid, int> _connections = [];
    private readonly Dictionary<Guid, Guid?> _online = [];
    private readonly Lock _lock = new();
    private readonly CancellationTokenSource _stop = new();
    private RedisPresence? _redis;

    internal TimeSpan Grace { get; set; } = TimeSpan.FromSeconds(5);

    public async Task<IReadOnlyList<Guid>> OnlineAsync(Guid? tenantId)
    {
        List<Guid> online;
        lock (_lock)
        {
            online = [.. _online.Where(o => o.Value == tenantId).Select(o => o.Key)];
        }

        return _redis is { } redis ? [.. online.Union(await redis.OnlineAsync(tenantId))] : online;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (services.GetService<IConnectionMultiplexer>() is { } connection)
            {
                _redis = new RedisPresence(connection, Snapshot, logger);
                _ = _redis.BeatAsync(_stop.Token);
            }
        }
        catch (RedisException exception)
        {
            logger.LogWarning(exception, "Redis is not reachable; users count per instance");
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stop.CancelAsync();
        if (_redis is { } redis)
        {
            await redis.StopAsync();
        }
    }

    internal void Connected((Guid? UserId, Guid? TenantId) who)
    {
        if (who.UserId is not { } userId)
        {
            return;
        }

        bool announce;
        lock (_lock)
        {
            _connections[userId] = _connections.GetValueOrDefault(userId) + 1;
            announce = _online.TryAdd(userId, who.TenantId);
        }

        if (announce)
        {
            _ = AnnounceAsync(userId, who.TenantId);
        }
    }

    internal void Disconnected((Guid? UserId, Guid? TenantId) who)
    {
        if (who.UserId is not { } userId)
        {
            return;
        }

        lock (_lock)
        {
            if ((_connections[userId] = _connections.GetValueOrDefault(userId) - 1) > 0)
            {
                return;
            }
        }

        _ = Task.Delay(Grace).ContinueWith(_ => LeaveAsync(userId, who.TenantId), TaskScheduler.Default).Unwrap();
    }

    // online elsewhere already: the organisation heard of it from that instance
    private async Task AnnounceAsync(Guid userId, Guid? tenantId)
    {
        if (_redis is { } redis && await redis.JoinAsync(userId, tenantId))
        {
            return;
        }

        await publisher.PublishAsync(tenantId, PresenceEvents.Topic, PresenceEvents.Changed, new PresenceDto(userId, true));
    }

    private async Task LeaveAsync(Guid userId, Guid? tenantId)
    {
        lock (_lock)
        {
            if (_connections.GetValueOrDefault(userId) > 0)
            {
                return;
            }

            _connections.Remove(userId);
            _online.Remove(userId);
        }

        if (_redis is { } redis && await redis.LeaveAsync(userId))
        {
            return;
        }

        await publisher.PublishAsync(tenantId, PresenceEvents.Topic, PresenceEvents.Changed, new PresenceDto(userId, false));
    }

    private IReadOnlyList<KeyValuePair<Guid, Guid?>> Snapshot()
    {
        lock (_lock)
        {
            return [.. _online];
        }
    }
}
