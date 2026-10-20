using System.Globalization;
using System.Web;
using Coworkee.Client.Blazor.Localization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;

namespace Coworkee.Client.Blazor.Components;

/// <summary>
/// The details of one item of a board or list in a drawer on the right (full screen on phones), bound to a query parameter (<c>?item={id}</c>):
/// links and the back button open and close it, Escape and the close button close it.
/// </summary>
public partial class CoworkeeDetailPanel<TKey> : IDisposable
    where TKey : IParsable<TKey>
{
    private ElementReference _root;
    private bool _focus;

    [Inject] private NavigationManager Nav { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    /// <summary>The query parameter that holds the key of the open item.</summary>
    [Parameter] public string QueryParameter { get; set; } = "item";

    /// <summary>The width on screens wider than that; phones get the whole screen.</summary>
    [Parameter] public string Width { get; set; } = "520px";

    [Parameter] public RenderFragment<TKey>? Title { get; set; }

    [Parameter] public RenderFragment<TKey>? Actions { get; set; }

    [Parameter] public RenderFragment<TKey>? ChildContent { get; set; }

    /// <summary>Raised with the key of the opened item, or the default when the panel closes.</summary>
    [Parameter] public EventCallback<TKey?> KeyChanged { get; set; }

    /// <summary>The key of the open item; the default when closed.</summary>
    public TKey? Key { get; private set; }

    public bool IsOpen { get; private set; }

    /// <summary>Opens the item and adds an entry to the history, so the back button closes it again.</summary>
    public void Open(TKey key) => Nav.NavigateTo(Nav.GetUriWithQueryParameter(QueryParameter, key.ToString()));

    /// <summary>Closes the panel and replaces the history entry of the item.</summary>
    public Task CloseAsync()
    {
        Nav.NavigateTo(Nav.GetUriWithQueryParameter(QueryParameter, (string?)null), replace: true);
        return Task.CompletedTask;
    }

    protected override void OnInitialized() => Nav.LocationChanged += OnLocationChanged;

    protected override Task OnParametersSetAsync() => FollowAsync();

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_focus && IsOpen)
        {
            _focus = false;
            await _root.FocusAsync();
        }
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs args) => _ = InvokeAsync(async () =>
    {
        await FollowAsync();
        StateHasChanged();
    });

    private async Task FollowAsync()
    {
        var value = HttpUtility.ParseQueryString(new Uri(Nav.Uri).Query)[QueryParameter];
        TKey? key = default;
        var open = !string.IsNullOrEmpty(value) && TKey.TryParse(value, CultureInfo.InvariantCulture, out key);
        if (open != IsOpen || !EqualityComparer<TKey?>.Default.Equals(key, Key))
        {
            (Key, IsOpen, _focus) = (open ? key : default, open, open);
            await KeyChanged.InvokeAsync(Key);
        }
    }

    private Task OpenChangedAsync(bool open) => open ? Task.CompletedTask : CloseAsync();

    private Task KeyDownAsync(KeyboardEventArgs args) => args.Key == "Escape" ? CloseAsync() : Task.CompletedTask;

    public void Dispose()
    {
        Nav.LocationChanged -= OnLocationChanged;
        GC.SuppressFinalize(this);
    }
}
