using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Reporting;

namespace Coworkee.Client.Blazor.Reporting.Api;

internal sealed class ReportsApi(HttpClient http) : ApiClientBase(http), IReportsApi
{
    private const string Root = "api/v1/reports";

    private IReadOnlyList<ReportSourceDto>? _sources;

    public async Task<IReadOnlyList<ReportSummaryDto>> GetReportsAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<ReportSummaryDto[]>(Root, cancellationToken);

    public Task<ReportDto> GetReportAsync(Guid id, CancellationToken cancellationToken = default) => GetAsync<ReportDto>($"{Root}/{id}", cancellationToken);

    public Task<ReportDto> CreateAsync(SaveReportRequest report, CancellationToken cancellationToken = default) =>
        SendAsync<ReportDto>(HttpMethod.Post, Root, report, cancellationToken);

    public Task<ReportDto> UpdateAsync(Guid id, SaveReportRequest report, CancellationToken cancellationToken = default) =>
        SendAsync<ReportDto>(HttpMethod.Put, $"{Root}/{id}", report, cancellationToken);

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => SendAsync(HttpMethod.Delete, $"{Root}/{id}", null, cancellationToken);

    public Task<ReportDto> DuplicateAsync(Guid id, CancellationToken cancellationToken = default) =>
        SendAsync<ReportDto>(HttpMethod.Post, $"{Root}/{id}/duplicate", null, cancellationToken);

    public Task SetFavoriteAsync(Guid id, bool favorite, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"{Root}/{id}/favorite", new FavoriteReportRequest(favorite), cancellationToken);

    public async Task<IReadOnlyList<ReportRoleDto>> GetRolesAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<ReportRoleDto[]>($"{Root}/roles", cancellationToken);

    // typed clients are transient: the cache lives as long as the page that asked
    public async Task<IReadOnlyList<ReportSourceDto>> GetSourcesAsync(CancellationToken cancellationToken = default) =>
        _sources ??= await GetAsync<ReportSourceDto[]>($"{Root}/sources", cancellationToken);

    public Task<ReportQueryResult> QueryAsync(ReportQueryRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<ReportQueryResult>(HttpMethod.Post, $"{Root}/query", request, cancellationToken);

    public async Task<byte[]> SheetAsync(ReportSheetRequest sheet, CancellationToken cancellationToken = default)
    {
        using var response = await SendCoreAsync(HttpMethod.Post, $"{Root}/sheet", sheet, cancellationToken);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }
}
