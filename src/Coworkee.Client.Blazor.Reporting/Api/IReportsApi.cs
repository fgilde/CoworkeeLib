using Coworkee.Contracts.Reporting;

namespace Coworkee.Client.Blazor.Reporting.Api;

public interface IReportsApi
{
    Task<IReadOnlyList<ReportSummaryDto>> GetReportsAsync(CancellationToken cancellationToken = default);

    Task<ReportDto> GetReportAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ReportDto> CreateAsync(SaveReportRequest report, CancellationToken cancellationToken = default);

    Task<ReportDto> UpdateAsync(Guid id, SaveReportRequest report, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ReportDto> DuplicateAsync(Guid id, CancellationToken cancellationToken = default);

    Task SetFavoriteAsync(Guid id, bool favorite, CancellationToken cancellationToken = default);

    Task<DashboardReportDto> GetDashboardAsync(CancellationToken cancellationToken = default);

    /// <summary>The user's own dashboard report, or with <paramref name="organisation"/> the default of the organisation; null clears it.</summary>
    Task SetDashboardAsync(Guid? reportId, bool organisation = false, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReportRoleDto>> GetRolesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReportSourceDto>> GetSourcesAsync(CancellationToken cancellationToken = default);

    Task<ReportQueryResult> QueryAsync(ReportQueryRequest request, CancellationToken cancellationToken = default);

    Task<byte[]> SheetAsync(ReportSheetRequest sheet, CancellationToken cancellationToken = default);
}
