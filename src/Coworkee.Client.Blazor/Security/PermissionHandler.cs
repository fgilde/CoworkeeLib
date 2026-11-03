using Microsoft.AspNetCore.Authorization;

namespace Coworkee.Client.Blazor.Security;

internal sealed class PermissionHandler(PermissionStore permissions) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (await permissions.HasAsync(requirement.Permission))
        {
            context.Succeed(requirement);
        }
    }
}
