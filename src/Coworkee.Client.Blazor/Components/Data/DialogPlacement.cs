using Microsoft.JSInterop;

namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>Where the user clicked last, so side sheets open on that side.</summary>
// ponytail: static module reference, fine for WebAssembly where one user owns the process; on Blazor Server it would need a scoped service
public static class DialogPlacement
{
    // several renderers in one process (tests) share the reference: one call at a time
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static IJSObjectReference? _module;

    public static async Task StartAsync(IJSRuntime js)
    {
        var module = await js.InvokeAsync<IJSObjectReference>("import", "./_content/Coworkee.Client.Blazor/coworkee.js");
        await Gate.WaitAsync();
        try
        {
            _module = module;
            await module.InvokeVoidAsync("trackPointer");
            await module.InvokeVoidAsync("guardNestedDialogs");
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task<bool> ClickedLeftAsync()
    {
        await Gate.WaitAsync();
        try
        {
            return _module is not null && await _module.InvokeAsync<bool>("pointerOnLeft");
        }
        catch (JSException)
        {
            return false;
        }
        finally
        {
            Gate.Release();
        }
    }
}
