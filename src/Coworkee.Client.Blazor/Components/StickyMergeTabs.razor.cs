using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Coworkee.Client.Blazor.Components;

/// <summary>MudTabs whose tab bar sticks below the app bar while scrolling and then shrinks to its tabs, so a sticky toolbar inside a panel shows next to it.</summary>
public partial class StickyMergeTabs : IAsyncDisposable
{
    private ElementReference _root;
    private ElementReference _sentinel;
    private IJSObjectReference? _observer;

    [Parameter] public RenderFragment? ChildContent { get; set; }

    [Parameter] public string? Class { get; set; }

    [Parameter(CaptureUnmatchedValues = true)] public Dictionary<string, object>? Attributes { get; set; }

    [Inject] private IJSRuntime JS { get; set; } = null!;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            var module = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/Coworkee.Client.Blazor/coworkee.js");
            _observer = await module.InvokeAsync<IJSObjectReference>("observeStuck", _sentinel, _root);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_observer is not null)
            {
                await _observer.InvokeVoidAsync("dispose");
                await _observer.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
        }
    }
}
