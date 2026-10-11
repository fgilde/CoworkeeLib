using Coworkee.Contracts.Realtime;

namespace Coworkee.Realtime;

/// <summary>
/// Users with at least one hub connection are online; the last connection gone, they turn offline after <see cref="Grace"/>, so a
/// reload or a reconnect does not flicker. Every change goes to the organisation on <see cref="PresenceEvents.Topic"/>.
/// </summary>
// ponytail: counts the connections of this instance; behind several instances a user counts per instance, a Redis hash when that matters
public sealed class RealtimePresence(IRealtimePublisher publisher)
{
    private readonly Dictionary<Guid, int> _connections = [];
    private readonly Dictionary<Guid, Guid?> _online = [];
    private readonly Lock _lock = new();

    internal TimeSpan Grace { get; set; } = TimeSpan.FromSeconds(5);

    public IReadOnlyList<Guid> Online(Guid? tenantId)
    {
        lock (_lock)
        {
            return [.. _online.Where(o => o.Value == tenantId).Select(o => o.Key)];
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
            _ = publisher.PublishAsync(who.TenantId, PresenceEvents.Topic, PresenceEvents.Changed, new PresenceDto(userId, true));
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

    private Task LeaveAsync(Guid userId, Guid? tenantId)
    {
        lock (_lock)
        {
            if (_connections.GetValueOrDefault(userId) > 0)
            {
                return Task.CompletedTask;
            }

            _connections.Remove(userId);
            _online.Remove(userId);
        }

        return publisher.PublishAsync(tenantId, PresenceEvents.Topic, PresenceEvents.Changed, new PresenceDto(userId, false));
    }
}
