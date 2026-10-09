using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json;
using Coworkee.Contracts.Realtime;
using Microsoft.AspNetCore.SignalR;

namespace Coworkee.Realtime;

/// <summary>
/// The hub connections of this instance, so the server can end a user's sessions: every instance ends its own connections, behind
/// a Redis backplane each one hears of it through its own channel (the session signal of the identity module).
/// </summary>
public sealed class RealtimeConnections(IHubContext<RealtimeHub> hub)
{
    /// <summary>Time the client gets to receive <see cref="RealtimeEventTypes.SessionRevoked"/> before its connection closes.</summary>
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(1);

    private readonly ConcurrentDictionary<string, HubCallerContext> _connections = new(StringComparer.Ordinal);

    internal void Add(HubCallerContext context) => _connections[context.ConnectionId] = context;

    internal void Remove(string connectionId) => _connections.TryRemove(connectionId, out _);

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
}
