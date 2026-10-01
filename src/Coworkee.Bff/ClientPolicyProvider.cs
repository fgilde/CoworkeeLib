using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Coworkee.Bff;

// Blazor pages carry client permission policies ("perm:..."). The BFF only ensures a session; the client and the API check the permission.
internal sealed class ClientPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    private static readonly AuthorizationPolicy Authenticated = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName) =>
        policyName.StartsWith("perm:", StringComparison.Ordinal) ? Authenticated : await base.GetPolicyAsync(policyName);
}
