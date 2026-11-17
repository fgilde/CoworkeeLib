using Coworkee.Client.Blazor.Layout;
using Coworkee.Client.Blazor.Navigation;
using Coworkee.Client.Blazor.Theming;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components;

/// <summary>The admin chrome (app bars, navigation drawer, assistant) inside <see cref="CoworkeeAppShell"/>, which does startup and providers.</summary>
public partial class CoworkeeLayout : IAsyncDisposable
{
    private bool _drawer = true;
    private readonly Guid _viewport = Guid.NewGuid();
    private bool _phone;

    // the layout lives on while the user navigates, so the first address decides: ?embed=1 shows the page without app bar, menu and assistant
    private bool _embedded;

    [Inject] private IBrowserViewportService Viewport { get; set; } = null!;

    [Inject] private NavigationManager Nav { get; set; } = null!;

    [Inject] private ThemeService ThemeService { get; set; } = null!;

    [Inject] private LayoutPreferences Preferences { get; set; } = null!;

    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;

    protected override void OnInitialized()
    {
        _embedded = System.Web.HttpUtility.ParseQueryString(new Uri(Nav.Uri).Query)["embed"] == "1";
        ThemeService.Changed += Refresh;
        Preferences.Changed += FollowPin;
        Nav.LocationChanged += CloseOnPhone;
    }

    // phones have no room for the mini strip: the menu is an overlay that closes after each navigation
    private bool Pinned => Preferences.Pinned && !_phone;

    // phones get one bar at the top
    private HeaderLayout Bars => _phone ? HeaderLayout.Default : HeaderLayout.From(ThemeService.ClientSettings);

    // the navigation carries the app's name and menu button when it runs beside the app bar or no bar runs across the window
    private bool DrawerHead => Pinned && (ThemeService.Theme.NavBesideAppBar || Bars.BrandBar is null);

    private DrawerClipMode ClipMode => Pinned && !DrawerHead && Bars.Has(HeaderPosition.Top) ? DrawerClipMode.Always : DrawerClipMode.Never;

    private string LayoutClass
    {
        get
        {
            var bars = Bars;
            string?[] classes =
            [
                bars.Has(HeaderPosition.Top) ? null : "cw-no-top-bar",
                bars.Has(HeaderPosition.Bottom) ? "cw-bar-bottom" : null,
                bars.Has(HeaderPosition.Left) ? "cw-bar-left" : null,
                bars.Has(HeaderPosition.Right) ? "cw-bar-right" : null,
            ];
            return string.Join(' ', classes.Where(c => c is not null));
        }
    }

    private void ToggleDrawer() => _drawer = !_drawer;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await Viewport.SubscribeAsync(_viewport, FollowViewport, new MudBlazor.Services.ResizeOptions { NotifyOnBreakpointOnly = true }, fireImmediately: true);
        }
    }

    public async ValueTask DisposeAsync()
    {
        ThemeService.Changed -= Refresh;
        Preferences.Changed -= FollowPin;
        Nav.LocationChanged -= CloseOnPhone;
        try
        {
            await Viewport.UnsubscribeAsync(_viewport);
        }
        catch (Microsoft.JSInterop.JSDisconnectedException)
        {
        }
    }

    private void FollowViewport(BrowserViewportEventArgs args)
    {
        // an unknown window size (no browser to measure) counts as wide
        var phone = args.Breakpoint == Breakpoint.Xs && args.BrowserWindowSize?.Width > 0;
        if (phone == _phone)
        {
            return;
        }

        _phone = phone;
        _drawer = !phone;
        InvokeAsync(StateHasChanged);
    }

    private void CloseOnPhone(object? sender, Microsoft.AspNetCore.Components.Routing.LocationChangedEventArgs args)
    {
        if (_phone && _drawer)
        {
            _drawer = false;
            InvokeAsync(StateHasChanged);
        }
    }

    private void FollowPin() => InvokeAsync(() =>
    {
        // unpinning a closed mini drawer would hide the menu completely, so it opens as overlay
        if (!Preferences.Pinned && !_drawer)
        {
            _drawer = true;
        }

        StateHasChanged();
    });

    // the bars and the drawer follow the header settings and the theme
    private void Refresh() => InvokeAsync(StateHasChanged);
}
