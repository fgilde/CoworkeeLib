using Microsoft.AspNetCore.Authorization;

namespace Coworkee.Client.Blazor.Security;

internal sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}
