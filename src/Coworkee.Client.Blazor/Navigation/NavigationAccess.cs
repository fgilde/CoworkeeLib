using System.Security.Claims;
using Coworkee.Client.Blazor.Security;
using Microsoft.AspNetCore.Authorization;

namespace Coworkee.Client.Blazor.Navigation;

/// <summary>The links a user may see: permission, feature, the item's own visibility and installation-wide pages only in the system organisation.</summary>
public static class NavigationAccess
{
    public static async Task<IReadOnlyList<CoworkeeNavItem>> AllowedAsync(
        IEnumerable<CoworkeeNavItem> items, ClaimsPrincipal user, IAuthorizationService authorization, Features.FeatureStore features, IServiceProvider? services = null)
    {
        var allowed = new List<CoworkeeNavItem>();
        foreach (var item in items)
        {
            if (!(item.HostOnly && SystemTenant.IsOutside(user))
                && (item.Permission is null || (await authorization.AuthorizeAsync(user, PermissionPolicy.For(item.Permission))).Succeeded)
                && (item.Feature is null || await features.IsEnabledAsync(item.Feature))
                && (item.Visible is null || (services is not null && await item.Visible(services))))
            {
                allowed.Add(item);
            }
        }

        return allowed;
    }

    /// <summary>The links at the current addresses of the library's pages; hidden ones (by old or new address) and turned off pages drop out.</summary>
    public static IReadOnlyList<CoworkeeNavItem> Linked(IEnumerable<CoworkeeNavItem> items, CoworkeePages pages, NavigationMenuOptions menu) =>
        items.Where(i => !menu.Hidden.Contains(i.Href))
            .Select(menu.Arrange)
            .Select(i => i.ForceLoad ? i : pages.Href(i.Href) is { } href ? i with { Href = href } : null)
            .OfType<CoworkeeNavItem>()
            .ToList();
}
