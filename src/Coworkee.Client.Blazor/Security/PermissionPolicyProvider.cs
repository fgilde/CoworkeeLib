using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Coworkee.Client.Blazor.Security;

internal sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName) =>
        policyName.StartsWith(PermissionPolicy.Prefix, StringComparison.Ordinal)
            ? new AuthorizationPolicyBuilder().RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(policyName[PermissionPolicy.Prefix.Length..])).Build()
            : await base.GetPolicyAsync(policyName);
}
