using System.Net.Http.Headers;
using System.Net.Http.Json;
using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Files;

namespace Coworkee.Client.Blazor.Files;

internal sealed class FilesApi(HttpClient http) : ApiClientBase(http), IFilesApi
{
    private const string Root = "api/v1/files";

    public Task<FolderContentDto> GetContentAsync(Guid? folderId, CancellationToken cancellationToken = default) =>
        GetAsync<FolderContentDto>($"{Root}/folders/content" + (folderId is { } id ? $"?folderId={id}" : string.Empty), cancellationToken);

    public Task<StoredFileDto> GetFileAsync(Guid id, CancellationToken cancellationToken = default) => GetAsync<StoredFileDto>($"{Root}/{id}", cancellationToken);

    public Task<FolderDto> CreateFolderAsync(CreateFolderRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<FolderDto>(HttpMethod.Post, $"{Root}/folders", request, cancellationToken);

    public Task<FolderDto> RenameFolderAsync(Guid id, string name, CancellationToken cancellationToken = default) =>
        SendAsync<FolderDto>(HttpMethod.Put, $"{Root}/folders/{id}/name", new RenameRequest(name), cancellationToken);

    public Task<StoredFileDto> RenameFileAsync(Guid id, string name, CancellationToken cancellationToken = default) =>
        SendAsync<StoredFileDto>(HttpMethod.Put, $"{Root}/{id}/name", new RenameRequest(name), cancellationToken);

    public Task MoveAsync(MoveRequest request, CancellationToken cancellationToken = default) => SendAsync(HttpMethod.Post, $"{Root}/move", request, cancellationToken);

    public Task DeleteAsync(FileSelectionRequest request, CancellationToken cancellationToken = default) => SendAsync(HttpMethod.Post, $"{Root}/delete", request, cancellationToken);

    public async Task<StoredFileDto> UploadAsync(Guid? folderId, string name, string? contentType, Stream content, CancellationToken cancellationToken = default)
    {
        using var body = new StreamContent(content);
        body.Headers.ContentType = MediaTypeHeaderValue.TryParse(contentType, out var type) ? type : new MediaTypeHeaderValue("application/octet-stream");
        var url = $"{Root}?name={Uri.EscapeDataString(name)}" + (folderId is { } id ? $"&folderId={id}" : string.Empty);
        using var response = await SendContentAsync(HttpMethod.Post, url, body, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<StoredFileDto>(cancellationToken))!;
    }

    public async Task<Stream> OpenReadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await SendContentAsync(HttpMethod.Get, FileUrls.Content(id), null, cancellationToken);
        var copy = new MemoryStream();
        await response.Content.CopyToAsync(copy, cancellationToken);
        copy.Position = 0;
        return copy;
    }
}
