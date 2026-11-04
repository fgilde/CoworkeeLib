using Coworkee.Client.Blazor.Layout;
using Coworkee.Client.Blazor.Navigation;
using Coworkee.Client.Blazor.Theming;
using Coworkee.Contracts.Ai;
using Coworkee.Contracts.Settings;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Extensions;

namespace Coworkee.Client.Blazor.Components;

/// <summary>The app bar in the order, places and visibility the administrator chose (<see cref="HeaderSettings.Items"/>); apps add items with <see cref="IAppBarContributor"/>.</summary>
public partial class CoworkeeAppBar : IDisposable
{
    private IReadOnlyList<(HeaderItem Item, HeaderPlacement Placement)> _arranged = [];
    private IReadOnlyDictionary<string, string?>? _settings;
    private CoworkeeCommandPalette? _palette;

    [Inject] private IEnumerable<IAppBarContributor> Contributors { get; set; } = null!;

    [Inject] private LayoutPreferences Preferences { get; set; } = null!;

    [Inject] private ThemeService Themes { get; set; } = null!;

    [Inject] private CoworkeePages Links { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;
    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;

    [Parameter] public bool Dark { get; set; }

    [Parameter] public EventCallback ToggleDrawer { get; set; }

    [Parameter] public EventCallback ToggleDark { get; set; }

    /// <summary>Phone width: the secondary actions sit in one overflow menu.</summary>
    [Parameter] public bool Compact { get; set; }

    private Task ToggleRightToLeftAsync() => Preferences.SetRightToLeftAsync(!Preferences.RightToLeft);

    private IReadOnlyList<HeaderItem> Bar => Placed(HeaderPlacement.Bar);

    private IReadOnlyList<HeaderItem> Overflow => Placed(HeaderPlacement.Overflow);

    private IReadOnlyList<HeaderItem> Placed(HeaderPlacement placement) =>
        _arranged.Where(a => Where(a) == placement && Shown(a.Item)).Select(a => a.Item).ToList();

    private HeaderPlacement Where((HeaderItem Item, HeaderPlacement Placement) entry) =>
        Compact && entry.Placement == HeaderPlacement.Bar && HeaderItems.FoldOnPhones.Contains(entry.Item.Id) ? HeaderPlacement.Overflow : entry.Placement;

    // the assistant's item is there only while the administrator chose the app bar for it
    private bool Shown(HeaderItem item) =>
        item.Id != HeaderItems.Assistant || Themes.ClientSettings.GetValueOrDefault(AiSettingNames.Display) == AssistantDisplay.AppBar;

    protected override void OnInitialized()
    {
        Preferences.Changed += Refresh;
        Themes.Changed += SettingsChanged;
        Arrange();
    }

    public void Dispose()
    {
        Preferences.Changed -= Refresh;
        Themes.Changed -= SettingsChanged;
    }

    private void Arrange()
    {
        _settings = Themes.ClientSettings;
        _arranged = HeaderItems.Arrange(HeaderItems.All(Contributors), HeaderItems.Parse(_settings.GetValueOrDefault(HeaderSettings.Items)));
    }

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
