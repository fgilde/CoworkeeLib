using System.Security.Claims;
using Coworkee.Application.Authorization;
using Coworkee.Contracts.Realtime;
using Coworkee.Core.Security;
using Coworkee.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

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

public static class RealtimeGroups
{
    public static string For(Guid? tenantId, string topic) =>
        topic.StartsWith("user:", StringComparison.Ordinal) || topic.StartsWith(RealtimeTopics.SessionPrefix, StringComparison.Ordinal)
        || topic.StartsWith(GlobalTopicAuthorizer.Prefix, StringComparison.Ordinal) ? topic : $"{tenantId}|{topic}";
}

public interface IRealtimeTopicAuthorizer
{
    bool Handles(string topic);

    Task<bool> AuthorizeAsync(string topic, CancellationToken cancellationToken);
}

internal sealed class UserTopicAuthorizer(ICurrentUser currentUser) : IRealtimeTopicAuthorizer
{
    public bool Handles(string topic) => topic.StartsWith("user:", StringComparison.Ordinal);

    public Task<bool> AuthorizeAsync(string topic, CancellationToken cancellationToken) =>
        Task.FromResult(currentUser.UserId is { } id && topic == RealtimeTopics.User(id));
}

/// <summary>Topics every signed-in user of every organisation may follow, e.g. "global:localization".</summary>
internal sealed class GlobalTopicAuthorizer(ICurrentUser currentUser) : IRealtimeTopicAuthorizer
{
    public const string Prefix = "global:";

    public bool Handles(string topic) => topic.StartsWith(Prefix, StringComparison.Ordinal);

    public Task<bool> AuthorizeAsync(string topic, CancellationToken cancellationToken) => Task.FromResult(currentUser.UserId is not null);
}

/// <summary>Topics every signed-in user follows within the own organisation, e.g. "tenant:users".</summary>
internal sealed class TenantTopicAuthorizer(ICurrentUser currentUser) : IRealtimeTopicAuthorizer
{
    public bool Handles(string topic) => topic.StartsWith("tenant:", StringComparison.Ordinal);

    public Task<bool> AuthorizeAsync(string topic, CancellationToken cancellationToken) => Task.FromResult(currentUser.UserId is not null && currentUser.TenantId is not null);
}

internal sealed class EntityTopicAuthorizer(CoworkeeDbContext db, IPermissionChecker permissions) : IRealtimeTopicAuthorizer
{
    public bool Handles(string topic) => topic.StartsWith("type:", StringComparison.Ordinal) || topic.StartsWith("entity:", StringComparison.Ordinal);

    public async Task<bool> AuthorizeAsync(string topic, CancellationToken cancellationToken)
    {
        var parts = topic.Split(':');
        var valid = parts is ["type", _] || (parts is ["entity", _, _] && parts[2].Length > 0);
        var attribute = valid
            ? db.Model.GetEntityTypes().Where(t => t.BaseType is null).Select(t => t.ClrType).FirstOrDefault(t => t.Name == parts[1])?
                .GetCustomAttributes(typeof(RealtimeAttribute), false).OfType<RealtimeAttribute>().FirstOrDefault()
            : null;
        if (attribute is null)
        {
            return false;
        }

        if (await permissions.IsGrantedAsync(attribute.Permission, cancellationToken))
        {
            return true;
        }

        return parts is ["entity", _, _] && attribute.ResourceType is { } resourceType && Guid.TryParse(parts[2], out var resourceId)
            && await permissions.IsGrantedAsync(attribute.Permission, resourceType, resourceId, cancellationToken);
    }
}
