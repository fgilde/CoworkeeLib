using Coworkee.Client.Blazor.Layout;
using Coworkee.Client.Blazor.Navigation;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;

namespace Coworkee.Client.Blazor.Components;

public partial class CoworkeeNavMenu : IDisposable
{
    private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);
    private string? _filter;
    private string? _open;

    [Inject] private IEnumerable<INavigationContributor> Contributors { get; set; } = null!;

    [Inject] private IOptions<NavigationMenuOptions> MenuOptions { get; set; } = null!;

    [Inject] private LayoutPreferences Preferences { get; set; } = null!;

    [Inject] private NavigationManager Nav { get; set; } = null!;
    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;


    /// <summary>The drawer shows icons only: groups flatten and the tools hide.</summary>
    [Parameter] public bool Mini { get; set; }

    private NavigationMenuOptions Options => MenuOptions.Value;

    private IEnumerable<CoworkeeNavItem> Items => Contributors.SelectMany(c => c.Items);

    protected override void OnInitialized()
    {
        Preferences.Changed += Refresh;
        var path = "/" + Nav.ToBaseRelativePath(Nav.Uri).Split('?', '#')[0];
        _open = NavigationTree.Groups(Items, Options)
            .FirstOrDefault(g => g.Items.Any(i => i.Href != "/" && path.StartsWith(i.Href, StringComparison.OrdinalIgnoreCase)))?.Title;
    }

    public void Dispose() => Preferences.Changed -= Refresh;

    private void Refresh() => InvokeAsync(StateHasChanged);

    private void Filter(string? filter) => _filter = filter;

    private bool IsExpanded(NavigationGroup group) =>
        !string.IsNullOrWhiteSpace(_filter) || (Preferences.SingleExpand ? group.Title == _open : !_collapsed.Contains(group.Title));

    private void SetExpanded(NavigationGroup group, bool expanded)
    {
        if (Preferences.SingleExpand)
        {
            _open = expanded ? group.Title : null;
        }
        else if (expanded)
        {
            _collapsed.Remove(group.Title);
        }
        else
        {
            _collapsed.Add(group.Title);
        }
    }
}
