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
public sealed class RealtimeHub(IEnumerable<IRealtimeTopicAuthorizer> authorizers) : Hub
{
    public override Task OnConnectedAsync()
    {
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

    public async Task Subscribe(string topic)
    {
        var (userId, tenantId) = Identity(Context.User);
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(userId, tenantId));
        var authorizer = authorizers.FirstOrDefault(a => a.Handles(topic));
        if (authorizer is null || !await authorizer.AuthorizeAsync(topic, Context.ConnectionAborted))
        {
            throw new HubException($"Not allowed to subscribe to '{topic}'.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.For(tenantId, topic), Context.ConnectionAborted);
    }

    public Task Unsubscribe(string topic) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, RealtimeGroups.For(Identity(Context.User).TenantId, topic), Context.ConnectionAborted);

    private static (Guid? UserId, Guid? TenantId) Identity(ClaimsPrincipal? user) =>
        (Guid.TryParse(user?.FindFirstValue("sub"), out var id) ? id : null, Guid.TryParse(user?.FindFirstValue("tenant"), out var tenant) ? tenant : null);
}

public static class RealtimeGroups
{
    public static string For(Guid? tenantId, string topic) =>
        topic.StartsWith("user:", StringComparison.Ordinal) ? topic : $"{tenantId}|{topic}";
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
