using Microsoft.JSInterop;

namespace Coworkee.Client.Blazor.Data;

public sealed class FileDownloader(IJSRuntime js) : IAsyncDisposable
{
    private IJSObjectReference? _module;

    public Task DownloadAsync(string fileName, string contentType, string content) => DownloadContentAsync(fileName, contentType, content);

    public Task DownloadAsync(string fileName, string contentType, byte[] content) => DownloadContentAsync(fileName, contentType, content);

    private async Task DownloadContentAsync(string fileName, string contentType, object content)
    {
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./_content/Coworkee.Client.Blazor/coworkee.js");
        await _module.InvokeVoidAsync("download", fileName, contentType, content);
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            await _module.DisposeAsync();
        }
    }
}
