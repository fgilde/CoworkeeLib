using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json;
using Coworkee.Contracts.Realtime;
using Coworkee.Core.Security;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Realtime;

/// <summary>
/// The hub connections of this instance and the topics each follows, so the server can end a user's sessions and take back topics
/// a user may no longer follow: every instance handles its own connections, behind a Redis backplane each one hears of it through
/// its own channel (the session signal of the identity module, <see cref="RealtimeAccessSignal"/>).
/// </summary>
public sealed class RealtimeConnections(IHubContext<RealtimeHub> hub, IServiceScopeFactory scopes, RealtimePresence presence)
{
    /// <summary>Time the client gets to receive <see cref="RealtimeEventTypes.SessionRevoked"/> before its connection closes.</summary>
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(1);

    private readonly ConcurrentDictionary<string, HubCallerContext> _connections = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _topics = new(StringComparer.Ordinal);

    /// <summary>Open connections of this instance.</summary>
    public int Count => _connections.Count;

    /// <summary>Signed-in users with an open connection to this instance.</summary>
    public int Users => _connections.Values.Select(c => Identity(c.User).UserId).OfType<Guid>().Distinct().Count();

    internal void Add(HubCallerContext context)
    {
        _connections[context.ConnectionId] = context;
        presence.Connected(Identity(context.User));
    }

    internal void Remove(string connectionId)
    {
        _topics.TryRemove(connectionId, out _);
        if (_connections.TryRemove(connectionId, out var context))
        {
            presence.Disconnected(Identity(context.User));
        }
    }

    internal void Follow(string connectionId, string topic) =>
        _topics.GetOrAdd(connectionId, _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal))[topic] = 0;

    internal void Unfollow(string connectionId, string topic)
    {
        if (_topics.TryGetValue(connectionId, out var topics))
        {
            topics.TryRemove(topic, out _);
        }
    }

    internal static (Guid? UserId, Guid? TenantId) Identity(ClaimsPrincipal? user) =>
        (Guid.TryParse(user?.FindFirstValue("sub"), out var id) ? id : null, Guid.TryParse(user?.FindFirstValue("tenant"), out var tenant) ? tenant : null);

    /// <summary>Sends <see cref="RealtimeEventTypes.SessionRevoked"/> to the user's connections <paramref name="keep"/> refuses and closes them.</summary>
    // ponytail: scans every connection of the instance; an index per user when an instance holds many thousands
    public async Task EndAsync(Guid userId, Func<ClaimsPrincipal, Task<bool>> keep, string reason, CancellationToken cancellationToken)
    {
        var subject = userId.ToString();
        var ended = new List<HubCallerContext>();
        foreach (var connection in _connections.Values.Where(c => c.User?.FindFirstValue("sub") == subject))
        {
            if (!await keep(connection.User!))
            {
                ended.Add(connection);
            }
        }

        if (ended.Count == 0)
        {
            return;
        }

        var envelope = new RealtimeEnvelope(RealtimeTopics.User(userId), RealtimeEventTypes.SessionRevoked,
            JsonSerializer.SerializeToElement(new SessionRevokedPayload(reason), JsonSerializerOptions.Web));
        await hub.Clients.Clients(ended.Select(c => c.ConnectionId).ToList()).SendAsync(RealtimeHubMethods.OnEvent, envelope, cancellationToken);
        _ = Task.Delay(Grace, CancellationToken.None).ContinueWith(_ => ended.ForEach(c => c.Abort()), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    /// <summary>
    /// Asks the topic authorizers again for every topic the connections of <paramref name="tenantId"/> (all, when null) follow, and
    /// takes the connection out of the groups it may no longer join, e.g. after a role was taken away or another role became active.
    /// </summary>
    // ponytail: one scope and one authorization per followed topic of every affected connection; batch per user when that gets expensive
    public async Task RecheckAsync(Guid? tenantId, CancellationToken cancellationToken)
    {
        foreach (var (connectionId, context) in _connections)
        {
            var (userId, tenant) = Identity(context.User);
            if ((tenantId is not null && tenant != tenantId) || !_topics.TryGetValue(connectionId, out var followed))
            {
                continue;
            }

            using var actor = CurrentUserScope.Begin(new ImpersonatedUser(userId, tenant));
            await using var scope = scopes.CreateAsyncScope();
            var authorizers = scope.ServiceProvider.GetServices<IRealtimeTopicAuthorizer>().ToList();
            foreach (var topic in followed.Keys)
            {
                if (!await RealtimeHub.IsAllowedAsync(authorizers, topic, cancellationToken))
                {
                    followed.TryRemove(topic, out _);
                    await hub.Groups.RemoveFromGroupAsync(connectionId, RealtimeGroups.For(tenant, topic), cancellationToken);
                }
            }
        }
    }
}
