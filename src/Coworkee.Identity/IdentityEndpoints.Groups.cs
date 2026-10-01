using Coworkee.Application.Messaging;
using Coworkee.AspNetCore.Http;
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;
using Coworkee.Identity.Groups;
using Coworkee.Identity.ResourcePermissions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Coworkee.Identity;

internal static partial class IdentityEndpoints
{
    static partial void MapGroups(RouteGroupBuilder api)
    {
        api.MapGet("/groups", ([AsParameters] PageRequest page, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetGroups(page), ct).ToHttpResult());
        api.MapPost("/groups", (GroupRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new CreateGroup(body), ct).ToHttpResult());
        api.MapPut("/groups/{id:guid}", (Guid id, GroupRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new UpdateGroup(id, body), ct).ToHttpResult());
        api.MapDelete("/groups/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new DeleteGroup(id), ct).ToHttpResult());
        api.MapPut("/groups/{id:guid}/members", (Guid id, IdListRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SetGroupMembers(id, body.Ids), ct).ToHttpResult());
        api.MapPut("/groups/{id:guid}/roles", (Guid id, IdListRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SetGroupRoles(id, body.Ids), ct).ToHttpResult());
    }

    static partial void MapResourcePermissions(RouteGroupBuilder api)
    {
        api.MapGet("/resource-permissions/{resourceType}/{resourceId:guid}", (string resourceType, Guid resourceId, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new GetResourcePermissions(resourceType, resourceId), ct).ToHttpResult());
        api.MapPost("/resource-permissions/{resourceType}/{resourceId:guid}", (string resourceType, Guid resourceId, GrantResourcePermissionRequest body, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new GrantResourcePermission(resourceType, resourceId, body), ct).ToHttpResult());
        api.MapDelete("/resource-permissions/{resourceType}/{resourceId:guid}/{id:guid}", (string resourceType, Guid resourceId, Guid id, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new RevokeResourcePermission(resourceType, resourceId, id), ct).ToHttpResult());
    }
}
