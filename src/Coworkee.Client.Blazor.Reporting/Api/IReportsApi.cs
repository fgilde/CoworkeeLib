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

    Task<IReadOnlyList<ReportGroupDto>> GetGroupsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReportSourceDto>> GetSourcesAsync(CancellationToken cancellationToken = default);

    Task<ReportQueryResult> QueryAsync(ReportQueryRequest request, CancellationToken cancellationToken = default);

    /// <summary>A page of rows of a computed source; entity sets page through OData.</summary>
    Task<ReportRowsResult> RowsAsync(ReportRowsRequest request, CancellationToken cancellationToken = default);

    Task<byte[]> SheetAsync(ReportSheetRequest sheet, CancellationToken cancellationToken = default);

    /// <summary>Whether a report can be built with AI; false when the server says no.</summary>
    Task<bool> IsAiAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>A definition the app's AI model built from the prompt (or from the current one), checked but not saved.</summary>
    Task<ReportDefinition> GenerateAsync(GenerateReportRequest request, CancellationToken cancellationToken = default);

    /// <summary>The signed-in user's subscriptions, or with <paramref name="all"/> those of the organisation (Reports.Subscriptions).</summary>
    Task<IReadOnlyList<ReportSubscriptionDto>> GetSubscriptionsAsync(bool all = false, CancellationToken cancellationToken = default);

    Task<ReportSubscriptionDto> SubscribeAsync(SaveReportSubscriptionRequest subscription, CancellationToken cancellationToken = default);

    Task<ReportSubscriptionDto> UpdateSubscriptionAsync(Guid id, SaveReportSubscriptionRequest subscription, CancellationToken cancellationToken = default);

    Task DeleteSubscriptionAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Mails the report now, as a test; the schedule stays.</summary>
    Task<ReportSubscriptionDto> SendSubscriptionAsync(Guid id, CancellationToken cancellationToken = default);
}
