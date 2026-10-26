using Coworkee.Application.Authorization;
using Coworkee.Contracts.Identity;

namespace Coworkee.Identity.Permissions;

internal sealed class IdentityPermissionDefinitions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context)
    {
        DefineIdentity(context);
        context.Group(ApiKeyPermissions.GroupName, "API keys")
            .Add(ApiKeyPermissions.Create, "Create own API keys")
            .Add(ApiKeyPermissions.Manage, "Manage the API keys of all users", ApiKeyPermissions.Create);
    }

    private static void DefineIdentity(PermissionDefinitionContext context) =>
        context.Group(IdentityPermissions.GroupName, "Identity")
            .Add(IdentityPermissions.Users.View, "View users")
            .Add(IdentityPermissions.Users.Manage, "Manage users", IdentityPermissions.Users.View)
            .Add(IdentityPermissions.Groups.View, "View groups")
            .Add(IdentityPermissions.Groups.ManageMembers, "Manage group members", IdentityPermissions.Groups.View, IdentityPermissions.Users.View)
            .Add(IdentityPermissions.Groups.Manage, "Manage groups", IdentityPermissions.Groups.View, IdentityPermissions.Users.View, IdentityPermissions.Groups.ManageMembers)
            .Add(IdentityPermissions.Groups.ScopeAll, "Work in any group")
            .Add(IdentityPermissions.Roles.View, "View roles")
            .Add(IdentityPermissions.Roles.Manage, "Manage roles", IdentityPermissions.Roles.View)
            .Add(IdentityPermissions.Permissions.Manage, "Manage permissions", IdentityPermissions.Roles.View)
            .Add(IdentityPermissions.ResourcePermissions.Manage, "Manage resource permissions", IdentityPermissions.Roles.View)
            .Add(IdentityPermissions.Clients.Manage, "Manage OpenID Connect clients");
}
