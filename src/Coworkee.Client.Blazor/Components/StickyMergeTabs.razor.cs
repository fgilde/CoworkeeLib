using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Coworkee.Client.Blazor.Components;

/// <summary>
/// Wraps a <c>MudTabs</c>: its tab bar sticks at <c>--cw-sticky-top</c> (below the top app bar, at the very top without one) with the surface as background and a shadow
/// while stuck; once a sticky toolbar of the open panel reaches the same line, the tabs shrink to their width and sit at its start (not on phones, there only the toolbar sticks).
/// </summary>
public partial class StickyMergeTabs : IAsyncDisposable
{
    private ElementReference _root;
    private ElementReference _sentinel;
    private IJSObjectReference? _observer;
    private bool _disposed;

    [Parameter] public RenderFragment? ChildContent { get; set; }

    [Inject] private IJSRuntime JS { get; set; } = null!;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        try
        {
            var module = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/Coworkee.Client.Blazor/coworkee.js");
            var observer = await module.InvokeAsync<IJSObjectReference>("observeStuck", _sentinel, _root);
            if (_disposed)
            {
                await StopAsync(observer);
            }
            else
            {
                _observer = observer;
            }
        }
        catch (JSDisconnectedException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        var observer = _observer;
        _observer = null;
        if (observer is not null)
        {
            await StopAsync(observer);
        }
    }

    private static async Task StopAsync(IJSObjectReference observer)
    {
        try
        {
            await observer.InvokeVoidAsync("dispose");
            await observer.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
        }
    }
}
