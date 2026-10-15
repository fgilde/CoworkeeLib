using System.Security.Claims;
using Coworkee.Client.Blazor.Security;
using Microsoft.AspNetCore.Authorization;

namespace Coworkee.Client.Blazor.Navigation;

/// <summary>The links a user may see: permission, feature and installation-wide pages only in the system organisation.</summary>
public static class NavigationAccess
{
    public static async Task<IReadOnlyList<CoworkeeNavItem>> AllowedAsync(
        IEnumerable<CoworkeeNavItem> items, ClaimsPrincipal user, IAuthorizationService authorization, Features.FeatureStore features)
    {
        var allowed = new List<CoworkeeNavItem>();
        foreach (var item in items)
        {
            if (!(item.HostOnly && SystemTenant.IsOutside(user))
                && (item.Permission is null || (await authorization.AuthorizeAsync(user, PermissionPolicy.For(item.Permission))).Succeeded)
                && (item.Feature is null || await features.IsEnabledAsync(item.Feature)))
            {
                allowed.Add(item);
            }
        }

        return allowed;
    }
}
