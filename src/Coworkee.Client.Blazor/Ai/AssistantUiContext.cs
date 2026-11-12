using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Layout;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Navigation;
using Coworkee.Client.Blazor.Theming;
using Coworkee.Contracts.Ai;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Options;

namespace Coworkee.Client.Blazor.Ai;

/// <summary>
/// What the user sees, sent with every message so the assistant can tell where things are and act in the UI: the pages of the navigation
/// the user may open (placed, hidden and translated like in the menu, with their <see cref="CoworkeeNavItem.Description"/>), the current
/// page, the appearance choices, the <see cref="IAssistantClientAction"/>s and the data sets the browser answers locally (<see cref="Data.IClientDataCache.LocalSets"/>).
/// </summary>
public sealed class AssistantUiContext(
    IEnumerable<INavigationContributor> contributors, IOptions<NavigationMenuOptions> menu, CoworkeePages pageLinks, AuthenticationStateProvider authentication,
    IAuthorizationService authorization, Features.FeatureStore features, IServiceProvider services, NavigationManager nav, ThemeService themes, LayoutPreferences layout,
    CoworkeeLocalizer l, IEnumerable<IAssistantClientAction> actions, Data.IClientDataCache? cache = null)
{
    public async Task<AiUiContext> BuildAsync(CancellationToken cancellationToken)
    {
        var options = menu.Value;
        var user = (await authentication.GetAuthenticationStateAsync()).User;
        var allowed = NavigationAccess.Linked(await NavigationAccess.AllowedAsync(contributors.SelectMany(c => c.Items), user, authorization, features, services), pageLinks, options);
        var pages = allowed.Select(i => new AiUiPage(l[i.Title], i.Href, Group(i.Group), l.Stored(i.Description))).ToList();
        if (options.ShowHome)
        {
            pages.Insert(0, new AiUiPage(l[options.HomeTitle], "/"));
        }

        var path = "/" + nav.ToBaseRelativePath(nav.Uri).Split('?', '#')[0];
        var current = pages.Where(p => p.Href == "/" ? path == "/" : path.StartsWith(p.Href, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => p.Href.Length)
            .FirstOrDefault();
        return new AiUiContext(
            pages, new AiUiPage(current?.Title ?? path, path, current?.Group), await ThemesAsync(cancellationToken), themes.Current?.Name, themes.Mode,
            [.. l.Languages.Where(x => x.IsEnabled).Select(x => x.Culture)], l.Culture, layout.RightToLeft,
            [.. actions.Select(a => new AiClientActionDto(a.Name, a.Description, a.Parameters))], cache?.LocalSets is { Count: > 0 } local ? local : null);
    }

    private string? Group(string? group) => group is null ? null : string.Join(" > ", group.Split(NavigationEntry.GroupSeparator).Select(g => l[g]));

    private async Task<IReadOnlyList<string>> ThemesAsync(CancellationToken cancellationToken) =>
        [.. (await themes.ThemesAsync().WaitAsync(cancellationToken)).Select(t => t.Name)];
}
