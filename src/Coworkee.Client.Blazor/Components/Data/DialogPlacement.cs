using Microsoft.JSInterop;

namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>Where the user clicked last, so side sheets open on that side.</summary>
// ponytail: static module reference, fine for WebAssembly where one user owns the process; on Blazor Server it would need a scoped service
public static class DialogPlacement
{
    private static IJSObjectReference? _module;

    public static async Task StartAsync(IJSRuntime js)
    {
        _module = await js.InvokeAsync<IJSObjectReference>("import", "./_content/Coworkee.Client.Blazor/coworkee.js");
        await _module.InvokeVoidAsync("trackPointer");
    }

    public static async Task<bool> ClickedLeftAsync()
    {
        try
        {
            return _module is not null && await _module.InvokeAsync<bool>("pointerOnLeft");
        }
        catch (JSException)
        {
            return false;
        }
    }
}
