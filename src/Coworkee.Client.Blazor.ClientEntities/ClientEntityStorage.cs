using Microsoft.JSInterop;

namespace Coworkee.Client.Blazor.ClientEntities;

/// <summary>Keeps the local copies between visits. The default writes to IndexedDB; replace it to keep them elsewhere or register <see cref="NoClientEntityStorage"/> to keep nothing.</summary>
public interface IClientEntityStorage
{
    Task<byte[]?> LoadAsync(string key);

    Task SaveAsync(string key, byte[] data);

    Task ClearAsync();
}

public sealed class NoClientEntityStorage : IClientEntityStorage
{
    public Task<byte[]?> LoadAsync(string key) => Task.FromResult<byte[]?>(null);

    public Task SaveAsync(string key, byte[] data) => Task.CompletedTask;

    public Task ClearAsync() => Task.CompletedTask;
}

internal sealed class IndexedDbClientEntityStorage(IJSRuntime js) : IClientEntityStorage, IAsyncDisposable
{
    private IJSObjectReference? _module;

    public async Task<byte[]?> LoadAsync(string key) => await (await ModuleAsync()).InvokeAsync<byte[]?>("load", key);

    public async Task SaveAsync(string key, byte[] data) => await (await ModuleAsync()).InvokeVoidAsync("save", key, data);

    public async Task ClearAsync() => await (await ModuleAsync()).InvokeVoidAsync("clear");

    private async Task<IJSObjectReference> ModuleAsync() =>
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./_content/Coworkee.Client.Blazor.ClientEntities/client-entities.js");

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            await _module.DisposeAsync();
        }
    }
}
