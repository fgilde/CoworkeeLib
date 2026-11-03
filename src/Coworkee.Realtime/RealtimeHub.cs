using System.Security.Claims;
using Coworkee.Contracts.Realtime;
using Coworkee.Core.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Coworkee.Realtime;

[Authorize]
public sealed class RealtimeHub(IEnumerable<IRealtimeTopicAuthorizer> authorizers, RealtimeConnections connections) : Hub
{
    public override Task OnConnectedAsync()
    {
        connections.Add(Context);
        if (long.TryParse(Context.User?.FindFirstValue("exp"), out var expires))
        {
            var context = Context;
            var remaining = DateTimeOffset.FromUnixTimeSeconds(expires) - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                context.Abort();
            }
            else
            {
                _ = Task.Delay(remaining, context.ConnectionAborted)
                    .ContinueWith(delay => { if (!delay.IsCanceled) { context.Abort(); } }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            }
        }

        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        connections.Remove(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    public async Task Subscribe(string topic)
    {
        var (userId, tenantId) = RealtimeConnections.Identity(Context.User);
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(userId, tenantId));
        if (!await IsAllowedAsync(authorizers, Context.User, topic, Context.ConnectionAborted))
        {
            throw new HubException($"Not allowed to subscribe to '{topic}'.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.For(tenantId, topic), Context.ConnectionAborted);
        connections.Follow(Context.ConnectionId, topic);
    }

    public async Task Unsubscribe(string topic)
    {
        connections.Unfollow(Context.ConnectionId, topic);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, RealtimeGroups.For(RealtimeConnections.Identity(Context.User).TenantId, topic), Context.ConnectionAborted);
    }

    // a session topic belongs to the connections whose token names that session; the authorizers see the user, not the token
    internal static async Task<bool> IsAllowedAsync(IEnumerable<IRealtimeTopicAuthorizer> authorizers, ClaimsPrincipal? user, string topic, CancellationToken cancellationToken) =>
        topic.StartsWith(RealtimeTopics.SessionPrefix, StringComparison.Ordinal)
            ? user?.FindFirstValue("sid") is { Length: > 0 } sid && topic == RealtimeTopics.Session(sid)
            : authorizers.FirstOrDefault(a => a.Handles(topic)) is { } authorizer && await authorizer.AuthorizeAsync(topic, cancellationToken);
}
