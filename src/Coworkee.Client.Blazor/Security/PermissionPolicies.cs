using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Coworkee.Client.Blazor.Security;

public static class PermissionPolicy
{
    public const string Prefix = "perm:";

    public static string For(string permission) => Prefix + permission;
}

internal sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

internal sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName) =>
        policyName.StartsWith(PermissionPolicy.Prefix, StringComparison.Ordinal)
            ? new AuthorizationPolicyBuilder().RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(policyName[PermissionPolicy.Prefix.Length..])).Build()
            : await base.GetPolicyAsync(policyName);
}

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
