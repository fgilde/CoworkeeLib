using Microsoft.JSInterop;

namespace Coworkee.Client.Blazor.ClientEntities;

// IDisposable too: the HttpClientFactory disposes the scope of its handlers (ClientEntityHttpHandler) synchronously
internal sealed class IndexedDbClientEntityStorage(IJSRuntime js) : IClientEntityStorage, IAsyncDisposable, IDisposable
{
    private IJSObjectReference? _module;

    public async Task<byte[]?> LoadAsync(string key) => await (await ModuleAsync()).InvokeAsync<byte[]?>("load", key);

    public async Task SaveAsync(string key, byte[] data) => await (await ModuleAsync()).InvokeVoidAsync("save", key, data);

    public async Task ClearAsync() => await (await ModuleAsync()).InvokeVoidAsync("clear");

    public async Task DeleteAsync(string prefix) => await (await ModuleAsync()).InvokeVoidAsync("remove", prefix);

    private async Task<IJSObjectReference> ModuleAsync() =>
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./_content/Coworkee.Client.Blazor.ClientEntities/client-entities.js");

    // each module is released once, whichever of the two disposals comes first
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _module, null) is { } module)
        {
            await module.DisposeAsync();
        }
    }

    // WebAssembly modules are in-process and dispose synchronously; elsewhere the release finishes in the background
    public void Dispose()
    {
        switch (Interlocked.Exchange(ref _module, null))
        {
            case IDisposable module:
                module.Dispose();
                break;
            case { } module:
                _ = Release(module);
                break;
        }
    }

    private static async Task Release(IJSObjectReference module)
    {
        try
        {
            await module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
        }
    }
}
