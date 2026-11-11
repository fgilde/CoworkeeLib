using Coworkee.Client.Blazor.Layout;
using Coworkee.Client.Blazor.Navigation;
using Coworkee.Client.Blazor.Security;
using Coworkee.Client.Blazor.Theming;
using Coworkee.Contracts.Ai;
using Coworkee.Contracts.Settings;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Extensions;

namespace Coworkee.Client.Blazor.Components;

/// <summary>
/// One app bar in the order, places and visibility the administrator chose (<see cref="HeaderSettings.Items"/>), at its edge of the window
/// (<see cref="HeaderLayout"/>); the layout renders a second one with <see cref="Second"/>. Apps add items with <see cref="IAppBarContributor"/>.
/// </summary>
public partial class CoworkeeAppBar : IDisposable
{
    private IReadOnlyList<(HeaderItem Item, HeaderPlacement Placement)> _arranged = [];
    private HeaderLayout _layout = HeaderLayout.Default;
    private IReadOnlyList<HeaderItemSetting> _restricted = [];
    private IReadOnlySet<string> _permitted = new HashSet<string>();
    private IReadOnlyDictionary<string, string?>? _settings;
    private CoworkeeCommandPalette? _palette;

    [Inject] private IEnumerable<IAppBarContributor> Contributors { get; set; } = null!;

    [Inject] private LayoutPreferences Preferences { get; set; } = null!;

    [Inject] private ThemeService Themes { get; set; } = null!;

    [Inject] private CoworkeePages Links { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private PermissionStore Permissions { get; set; } = null!;
    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;

    [Parameter] public bool Dark { get; set; }

    [Parameter] public EventCallback ToggleDrawer { get; set; }

    [Parameter] public EventCallback ToggleDark { get; set; }

    /// <summary>Phone width: one bar at the top, the secondary actions and the second bar's items sit in its overflow menu.</summary>
    [Parameter] public bool Compact { get; set; }

    /// <summary>The second app bar: the items placed there (<see cref="HeaderPlacement.SecondBar"/>, <see cref="HeaderPlacement.SecondOverflow"/>).</summary>
    [Parameter] public bool Second { get; set; }

    [Parameter] public HeaderPosition Position { get; set; } = HeaderPosition.Top;

    /// <summary>The menu button and the app's name at the start of the bar (a bar across the window only).</summary>
    [Parameter] public bool ShowBrand { get; set; } = true;

    /// <summary>The menu button with the name; off while the navigation carries its own.</summary>
    [Parameter] public bool ShowMenuButton { get; set; } = true;

    private string TestId => Second ? "app-bar-2" : "app-bar";

    private Task ToggleRightToLeftAsync() => Preferences.SetRightToLeftAsync(!Preferences.RightToLeft);

    private IReadOnlyList<HeaderItem> Bar => Placed(Second ? HeaderPlacement.SecondBar : HeaderPlacement.Bar);

    private IReadOnlyList<HeaderItem> Overflow => Placed(Second ? HeaderPlacement.SecondOverflow : HeaderPlacement.Overflow);

    private bool HasSpacer => Bar.Any(i => HeaderItems.IsSpacer(i.Id));

    // each item with the end of the bar it sits at: after a spacer the far end; without one the end across the window, the top at the side
    private List<(HeaderItem Item, bool End)> Laid(bool vertical)
    {
        var end = !vertical && !HasSpacer;
        var laid = new List<(HeaderItem, bool)>();
        foreach (var item in Bar)
        {
            end |= HeaderItems.IsSpacer(item.Id);
            laid.Add((item, end));
        }

        return laid;
    }

    // the hidden palette keeps Ctrl+K and opens from the overflow menu: in the bar whose overflow menu has the search, in the first one when it is hidden
    private bool HasHiddenPalette => _arranged.FirstOrDefault(a => a.Item.Id == HeaderItems.Search) is { Item: not null } search
        && (Where(search) == (Second ? HeaderPlacement.SecondOverflow : HeaderPlacement.Overflow) || (!Second && Where(search) == HeaderPlacement.Hidden));

    private bool HasPalette => HasHiddenPalette || Bar.Any(i => i.Id == HeaderItems.Search);

    private IReadOnlyList<HeaderItem> Placed(HeaderPlacement placement) =>
        _arranged.Where(a => Where(a) == placement && Shown(a.Item)).Select(a => a.Item).ToList();

    private HeaderPlacement Where((HeaderItem Item, HeaderPlacement Placement) entry)
    {
        var placement = _layout.Effective(entry.Placement);
        if (!Compact)
        {
            return placement;
        }

        return placement switch
        {
            HeaderPlacement.Bar when HeaderItems.FoldOnPhones.Contains(entry.Item.Id) => HeaderPlacement.Overflow,
            HeaderPlacement.SecondBar when entry.Item.Id == HeaderItems.User => HeaderPlacement.Bar,
            HeaderPlacement.SecondBar or HeaderPlacement.SecondOverflow => HeaderPlacement.Overflow,
            _ => placement,
        };
    }

    // the assistant's item is there only while the administrator chose the app bar for it
    private bool Shown(HeaderItem item) =>
        (item.Id != HeaderItems.Assistant || Themes.ClientSettings.GetValueOrDefault(AiSettingNames.Display) == AssistantDisplay.AppBar)
        && (_permitted.Contains(item.Id) || !_restricted.Any(r => r.Id == item.Id));

    protected override void OnInitialized()
    {
        Preferences.Changed += Refresh;
        Themes.Changed += SettingsChanged;
        Permissions.Changed += PermissionsChanged;
        Arrange();
    }

    public void Dispose()
    {
        Preferences.Changed -= Refresh;
        Themes.Changed -= SettingsChanged;
        Permissions.Changed -= PermissionsChanged;
    }

    private void Arrange()
    {
        _settings = Themes.ClientSettings;
        _layout = HeaderLayout.From(_settings);
        var configured = HeaderItems.Parse(_settings.GetValueOrDefault(HeaderSettings.Items));
        _arranged = HeaderItems.Arrange(HeaderItems.All(Contributors), configured);
        // the account menu is for everyone: without it nobody signs out
        _restricted = configured.Where(c => c.Id != HeaderItems.User && c.Permissions is { Count: > 0 }).DistinctBy(c => c.Id).ToList();
        _ = CheckPermissionsAsync();
    }

    // restricted items stay hidden until the check allows them, so nobody sees an item flash that is not for them
    private async Task CheckPermissionsAsync()
    {
        var restricted = _restricted;
        if (restricted.Count == 0)
        {
            return;
        }

        var permitted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in restricted)
        {
            if (await Permissions.HasAsync(item.Permissions!, item.Match))
            {
                permitted.Add(item.Id);
            }
        }

        if (ReferenceEquals(restricted, _restricted))
        {
            _permitted = permitted;
            Refresh();
        }
    }

    private void PermissionsChanged() => _ = CheckPermissionsAsync();

    private void SettingsChanged()
    {
        if (!ReferenceEquals(_settings, Themes.ClientSettings))
        {
            Arrange();
        }

        Refresh();
    }

    private void Refresh() => InvokeAsync(StateHasChanged);

    private Task OpenSearchAsync() => _palette?.OpenAsync() ?? Task.CompletedTask;

    private Task ShowAboutAsync() => Dialogs.ShowExAsync<AboutDialog>(string.Empty, new DialogParameters(), Data.EditDialogExtensions.RightSheet());
}
