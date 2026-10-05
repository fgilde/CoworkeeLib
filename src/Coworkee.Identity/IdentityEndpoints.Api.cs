using Coworkee.Application.Messaging;
using Coworkee.AspNetCore.Http;
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;
using Coworkee.Identity.Permissions;
using Coworkee.Identity.Roles;
using Coworkee.Identity.Users;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Coworkee.Identity;

internal static partial class IdentityEndpoints
{
    static partial void MapIdentityApi(RouteGroupBuilder api)
    {
        MapUsers(api);
        MapRoles(api);
        MapGrants(api);
        MapGroups(api);
        MapResourcePermissions(api);
    }

    private static void MapUsers(RouteGroupBuilder api)
    {
        api.MapGet("/users", ([AsParameters] PageRequest page, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetUsers(page), ct).ToHttpResult());
        api.MapPost("/users", (CreateUserRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new CreateUser(body), ct).ToHttpResult());
        api.MapGet("/users/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetUser(id), ct).ToHttpResult());
        api.MapPost("/users/{id:guid}/unlock", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new UnlockUser(id), ct).ToHttpResult());
        api.MapPut("/users/{id:guid}", (Guid id, UpdateUserRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new UpdateUser(id, body), ct).ToHttpResult());
        api.MapGet("/users/{id:guid}/permissions", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetEffectivePermissions(id), ct).ToHttpResult());
        api.MapPut("/users/{id:guid}/roles", (Guid id, IdListRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SetUserRoles(id, body.Ids), ct).ToHttpResult());
    }

    private static void MapRoles(RouteGroupBuilder api)
    {
        api.MapGet("/roles", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetRoles(), ct).ToHttpResult());
        api.MapPost("/roles", (RoleRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new CreateRole(body), ct).ToHttpResult());
        api.MapPut("/roles/{id:guid}", (Guid id, RoleRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new UpdateRole(id, body), ct).ToHttpResult());
        api.MapDelete("/roles/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new DeleteRole(id), ct).ToHttpResult());
    }

    private static void MapGrants(RouteGroupBuilder api)
    {
        api.MapGet("/permissions/definitions", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetPermissionDefinitions(), ct).ToHttpResult());
        api.MapGet("/permissions/grants/{providerType}/{providerKey:guid}", (PermissionProviderType providerType, Guid providerKey, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new GetGrants(providerType, providerKey), ct).ToHttpResult());
        api.MapPut("/permissions/grants/{providerType}/{providerKey:guid}", (PermissionProviderType providerType, Guid providerKey, NameListRequest body, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new SetGrants(providerType, providerKey, body.Names), ct).ToHttpResult());
    }

    static partial void MapGroups(RouteGroupBuilder api);

    static partial void MapResourcePermissions(RouteGroupBuilder api);
}
