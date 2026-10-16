using Microsoft.AspNetCore.Authorization;
using Coworkee.Client.Blazor.Layout;
using Coworkee.Client.Blazor.Navigation;
using Coworkee.Client.Blazor.Security;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.Options;
using MudBlazor.Extensions.Core.Enums;
using Nextended.Core.Extensions;

namespace Coworkee.Client.Blazor.Components;

public partial class CoworkeeNavMenu : IDisposable
{
    private static readonly TreeViewMode[] ToggleableViewModes = [TreeViewMode.Default, TreeViewMode.List, TreeViewMode.FlatList];
    private HashSet<NavigationEntry>? _entries;
    private NavigationEntry? _selected;
    private TreeViewMode _viewMode = TreeViewMode.Default;

    [Inject] private IEnumerable<INavigationContributor> Contributors { get; set; } = null!;

    [Inject] private CoworkeePages Pages { get; set; } = null!;

    [Inject] private IOptions<NavigationMenuOptions> MenuOptions { get; set; } = null!;

    [Inject] private LayoutPreferences Preferences { get; set; } = null!;

    [Inject] private Theming.ThemeService ThemeService { get; set; } = null!;

    [Inject] private PermissionStore Permissions { get; set; } = null!;

    [Inject] private NavigationBadges Badges { get; set; } = null!;

    [Inject] private Features.FeatureStore Features { get; set; } = null!;

    [Inject] private Microsoft.AspNetCore.Authorization.IAuthorizationService Authorization { get; set; } = null!;

    [CascadingParameter] private Task<Microsoft.AspNetCore.Components.Authorization.AuthenticationState>? AuthenticationState { get; set; }

    [Inject] private NavigationManager Nav { get; set; } = null!;

    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;

    /// <summary>The drawer shows icons only: the tree becomes a flat icon list and the tools hide.</summary>
    [Parameter] public bool Mini { get; set; }

    private TreeViewMode ViewMode => Mini ? TreeViewMode.FlatList : _viewMode;

    private Theming.CoworkeeTheme Theme => ThemeService.Theme;

    private bool SingleExpand => Preferences.SingleExpand ?? Theme.NavSingleExpand;

    private Dictionary<string, object> TreeParameters => new()
    {
        ["ExpandBehaviour"] = SingleExpand ? TreeViewExpandBehaviour.SingleExpand : TreeViewExpandBehaviour.Default,
        ["RenderHomeLink"] = false,
        ["BackLinkLabel"] = L["Back"],
    };

    protected override async Task OnInitializedAsync()
    {
        Preferences.Changed += Refresh;
        ThemeService.Changed += Refresh;
        Permissions.Changed += Reload;
        Features.Changed += Reload;
        Nav.LocationChanged += LocationChanged;
        Badges.Changed += Refresh;
        await BuildAsync();
    }

    public void Dispose()
    {
        Preferences.Changed -= Refresh;
        ThemeService.Changed -= Refresh;
        Permissions.Changed -= Reload;
        Features.Changed -= Reload;
        Nav.LocationChanged -= LocationChanged;
        Badges.Changed -= Refresh;
    }

    private async Task BuildAsync()
    {
        var options = MenuOptions.Value;
        var user = AuthenticationState is null ? new System.Security.Claims.ClaimsPrincipal() : (await AuthenticationState).User;
        var allowed = NavigationAccess.Linked(await NavigationAccess.AllowedAsync(Contributors.SelectMany(c => c.Items), user, Authorization, Features), Pages, options);

        _entries = NavigationEntry.Build(allowed, options, options.ShowHome ? options.HomeTitle : null);
        SelectCurrent();
    }

    private void SelectCurrent()
    {
        var path = "/" + Nav.ToBaseRelativePath(Nav.Uri).Split('?', '#')[0];
        _selected = _entries?.Recursive(e => e.Children ?? [])
            .Where(e => e.Href is { Length: > 0 } href && (href == "/" ? path == "/" : path.StartsWith(href, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(e => e.Href!.Length)
            .FirstOrDefault();
    }

    private static bool HasLink(NavigationEntry? entry) => !string.IsNullOrWhiteSpace(entry?.Href);

    // the tree reports the entry SelectCurrent chose as a selection too; going there would drop the query or leave a detail page
    private Task SelectAsync(NavigationEntry? entry)
    {
        if (HasLink(entry) && entry != _selected)
        {
            _selected = entry;
            Nav.NavigateTo(entry!.Href!, entry.ForceLoad);
        }

        return Task.CompletedTask;
    }

    private void SetViewMode(TreeViewMode mode)
    {
        if (!Mini)
        {
            _viewMode = mode;
        }
    }

    private void LocationChanged(object? sender, LocationChangedEventArgs e)
    {
        SelectCurrent();
        Refresh();
    }

    private void Refresh() => InvokeAsync(StateHasChanged);

    private void Reload() => InvokeAsync(async () =>
    {
        await BuildAsync();
        StateHasChanged();
    });
}
