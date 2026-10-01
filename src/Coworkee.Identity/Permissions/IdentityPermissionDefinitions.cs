using Coworkee.Application.Authorization;
using Coworkee.Contracts.Identity;

namespace Coworkee.Identity.Permissions;

internal sealed class IdentityPermissionDefinitions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) =>
        context.Group(IdentityPermissions.GroupName, "Identity")
            .Add(IdentityPermissions.Users.View, "View users")
            .Add(IdentityPermissions.Users.Manage, "Manage users", IdentityPermissions.Users.View)
            .Add(IdentityPermissions.Groups.View, "View groups")
            .Add(IdentityPermissions.Groups.Manage, "Manage groups", IdentityPermissions.Groups.View, IdentityPermissions.Users.View)
            .Add(IdentityPermissions.Roles.View, "View roles")
            .Add(IdentityPermissions.Roles.Manage, "Manage roles", IdentityPermissions.Roles.View)
            .Add(IdentityPermissions.Permissions.Manage, "Manage permissions", IdentityPermissions.Roles.View)
            .Add(IdentityPermissions.ResourcePermissions.Manage, "Manage resource permissions", IdentityPermissions.Roles.View);
}
