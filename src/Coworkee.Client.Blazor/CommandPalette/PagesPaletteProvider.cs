using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Navigation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Options;

namespace Coworkee.Client.Blazor.CommandPalette;

/// <summary>The pages of the navigation the user may open, placed and hidden like in the menu.</summary>
internal sealed class PagesPaletteProvider(
    IEnumerable<INavigationContributor> contributors, IOptions<NavigationMenuOptions> menu, AuthenticationStateProvider authentication, CoworkeePages pageLinks,
    IAuthorizationService authorization, Features.FeatureStore features, CoworkeeLocalizer l, NavigationManager nav, IServiceProvider services) : ICommandPaletteProvider
{
    public const string Group = "Pages";

    public async Task<IEnumerable<CommandPaletteItem>> GetItemsAsync(string query, CancellationToken cancellationToken)
    {
        var options = menu.Value;
        var user = (await authentication.GetAuthenticationStateAsync()).User;
        var allowed = NavigationAccess.Linked(await NavigationAccess.AllowedAsync(contributors.SelectMany(c => c.Items), user, authorization, features, services), pageLinks, options);
        var pages = allowed.Select(options.Arrange)
            .Where(i => !options.Hidden.Contains(i.Href))
            .Select(i => new CommandPaletteItem(Group, l[i.Title])
            {
                Icon = i.Icon,
                Href = i.Href,
                Subtitle = i.Group is null ? null : string.Join(" / ", i.Group.Split(NavigationEntry.GroupSeparator).Select(g => l[g])),
                Keywords = i.Title,
                Run = i.ForceLoad ? () => Leave(i.Href) : null,
            });
        return options.ShowHome
            ? pages.Prepend(new CommandPaletteItem(Group, l[options.HomeTitle]) { Icon = MudBlazor.Icons.Material.Outlined.Home, Href = "/" })
            : pages;
    }

    // pages outside the app (e.g. the API docs) need a full page load
    private Task Leave(string href)
    {
        nav.NavigateTo(href, forceLoad: true);
        return Task.CompletedTask;
    }
}
