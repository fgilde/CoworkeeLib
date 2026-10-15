using System.Net;
using System.Net.Http.Json;
using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Data;

namespace Coworkee.Client.Blazor.Data;

internal sealed class DataTransferApi(HttpClient http) : ApiClientBase(http), IDataTransferApi
{
    private const string Root = "api/v1/data";

    public async Task<IReadOnlyList<ImportTargetDto>> GetTargetsAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<ImportTargetDto[]>($"{Root}/imports", cancellationToken);

    public Task<ImportTargetDto> GetTargetAsync(string entitySet, CancellationToken cancellationToken = default) =>
        GetAsync<ImportTargetDto>($"{Root}/{Uri.EscapeDataString(entitySet)}/import", cancellationToken);

    public async Task<ImportSheetDto> UploadAsync(string entitySet, Stream content, string fileName, CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent { { new StreamContent(content), "file", fileName } };
        using var response = await SendContentAsync(HttpMethod.Post, $"{Root}/{Uri.EscapeDataString(entitySet)}/import/file", form, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<ImportSheetDto>(cancellationToken))!;
    }

    public Task<ImportSheetDto> ReadAgainAsync(Guid jobId, ImportParseOptions options, CancellationToken cancellationToken = default) =>
        SendAsync<ImportSheetDto>(HttpMethod.Post, $"{Root}/imports/{jobId}/sheet", options, cancellationToken);

    public Task<ImportPreviewDto> PreviewAsync(Guid jobId, ImportSettings settings, CancellationToken cancellationToken = default) =>
        SendAsync<ImportPreviewDto>(HttpMethod.Post, $"{Root}/imports/{jobId}/preview", settings, cancellationToken);

    public Task<DataTransferJobDto> StartAsync(Guid jobId, ImportSettings settings, CancellationToken cancellationToken = default) =>
        SendAsync<DataTransferJobDto>(HttpMethod.Post, $"{Root}/imports/{jobId}/start", settings, cancellationToken);

    public Task<DataTransferJobDto> UndoAsync(Guid jobId, CancellationToken cancellationToken = default) =>
        SendAsync<DataTransferJobDto>(HttpMethod.Post, $"{Root}/imports/{jobId}/undo", null, cancellationToken);

    public async Task<IReadOnlyList<ImportProfileDto>> GetProfilesAsync(string entitySet, CancellationToken cancellationToken = default) =>
        await GetAsync<ImportProfileDto[]>($"{Root}/{Uri.EscapeDataString(entitySet)}/import/profiles", cancellationToken);

    public Task<ImportProfileDto> SaveProfileAsync(string entitySet, SaveImportProfileRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<ImportProfileDto>(HttpMethod.Post, $"{Root}/{Uri.EscapeDataString(entitySet)}/import/profiles", request, cancellationToken);

    public Task DeleteProfileAsync(Guid id, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"{Root}/import-profiles/{id}", null, cancellationToken);

    public async Task<IReadOnlyList<DataTransferJobDto>> GetJobsAsync(string? entitySet = null, CancellationToken cancellationToken = default) =>
        await GetAsync<DataTransferJobDto[]>(entitySet is null ? $"{Root}/jobs" : $"{Root}/jobs?entitySet={Uri.EscapeDataString(entitySet)}", cancellationToken);

    public Task<DataTransferJobDto> GetJobAsync(Guid id, CancellationToken cancellationToken = default) =>
        GetAsync<DataTransferJobDto>($"{Root}/jobs/{id}", cancellationToken);

    public async Task<byte[]> DownloadAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        using var response = await SendContentAsync(HttpMethod.Get, $"{Root}/jobs/{jobId}/file", null, cancellationToken);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    public async Task<ExportResult> ExportAsync(
        string entitySet, ExportFileFormat format, ODataQuery query, IReadOnlyCollection<string> columns, CancellationToken cancellationToken = default)
    {
        var url = $"{Root}/{Uri.EscapeDataString(entitySet)}/export{(query with { Count = false, Top = null, Skip = null, Expand = null, Select = null }).ToQueryString()}";
        url += (url.Contains('?') ? "&" : "?") + $"format={format.ToString().ToLowerInvariant()}"
               + (columns.Count == 0 ? string.Empty : $"&columns={Uri.EscapeDataString(string.Join(",", columns))}");
        using var response = await SendContentAsync(HttpMethod.Get, url, null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Accepted)
        {
            var job = await response.Content.ReadFromJsonAsync<DataTransferJobDto>(cancellationToken);
            return new ExportResult(null, job!.FileName ?? entitySet, string.Empty, job);
        }

        var name = response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? entitySet;
        return new ExportResult(await response.Content.ReadAsByteArrayAsync(cancellationToken), name, response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream", null);
    }
}
