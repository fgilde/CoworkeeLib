using MudBlazor.Extensions;
using Coworkee.Client.Blazor.Layout;
using Coworkee.Client.Blazor.Navigation;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components;

public partial class CoworkeeAppBar : IDisposable
{
    [Inject] private IEnumerable<IAppBarContributor> Contributors { get; set; } = null!;

    [Inject] private LayoutPreferences Preferences { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;
    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;

    [Parameter] public bool Dark { get; set; }

    [Parameter] public EventCallback ToggleDrawer { get; set; }

    [Parameter] public EventCallback ToggleDark { get; set; }

    /// <summary>Phone width: the secondary actions sit in one overflow menu.</summary>
    [Parameter] public bool Compact { get; set; }

    private Task ToggleRightToLeftAsync() => Preferences.SetRightToLeftAsync(!Preferences.RightToLeft);

    private IEnumerable<AppBarItem> Items => Contributors.SelectMany(c => c.Items).OrderBy(i => i.Order);

    protected override void OnInitialized() => Preferences.Changed += Refresh;

    public void Dispose() => Preferences.Changed -= Refresh;

    private void Refresh() => InvokeAsync(StateHasChanged);

    private Task ShowAboutAsync() => Dialogs.ShowExAsync<AboutDialog>(string.Empty, new DialogParameters(), Data.EditDialogExtensions.RightSheet());
}
