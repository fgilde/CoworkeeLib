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
        api.MapGet("/groups/children", (Guid? parentId, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetGroupChildren(parentId), ct).ToHttpResult());
        api.MapGet("/groups/tree", (string? search, IDispatcher d, CancellationToken ct) => d.SendAsync(new SearchGroupTree(search ?? ""), ct).ToHttpResult());
        api.MapGet("/groups/default", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetDefaultGroup(), ct).ToHttpResult());
        api.MapPut("/groups/default", (DefaultGroupDto body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SetDefaultGroup(body.GroupId), ct).ToHttpResult());
        api.MapGet("/groups/mine", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetMyGroups(), ct).ToHttpResult());
        api.MapPut("/groups/mine/selection", (IdListRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SelectGroups(body.Ids), ct).ToHttpResult());
        api.MapPost("/groups", (GroupRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new CreateGroup(body), ct).ToHttpResult());
        api.MapPut("/groups/{id:guid}", (Guid id, GroupRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new UpdateGroup(id, body), ct).ToHttpResult());
        api.MapDelete("/groups/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new DeleteGroup(id), ct).ToHttpResult());
        api.MapPut("/groups/{id:guid}/parent", (Guid id, MoveGroupRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new MoveGroup(id, body.ParentId), ct).ToHttpResult());
        api.MapPut("/groups/{id:guid}/members", (Guid id, IdListRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SetGroupMembers(id, body.Ids), ct).ToHttpResult());
        api.MapPost("/groups/{id:guid}/members/add", (Guid id, IdListRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new AddGroupMembers(id, body.Ids), ct).ToHttpResult());
        api.MapPost("/groups/{id:guid}/members/remove", (Guid id, IdListRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new RemoveGroupMembers(id, body.Ids), ct).ToHttpResult());
        api.MapPost("/groups/{id:guid}/members/move", (Guid id, MoveMembersRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new MoveGroupMembers(id, body.ToId), ct).ToHttpResult());
        api.MapPut("/groups/{id:guid}/owners", (Guid id, IdListRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SetGroupOwners(id, body.Ids), ct).ToHttpResult());
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
