using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Reporting.Api;
using Coworkee.Contracts.Reporting;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Reporting.Components;

/// <summary>Menu entries that make a report the user's dashboard or, with Reports.Dashboard, the organisation's.</summary>
public partial class DashboardMenuItems
{
    [Inject] private IReportsApi Api { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Parameter, EditorRequired] public Guid ReportId { get; set; }

    [Parameter] public DashboardReportDto? Dashboard { get; set; }

    [Parameter] public EventCallback<DashboardReportDto> DashboardChanged { get; set; }

    private async Task SetAsync(Guid? reportId, bool organisation)
    {
        try
        {
            await Api.SetDashboardAsync(reportId, organisation);
            Snackbar.Add(L["Dashboard changed."], Severity.Success);
            await DashboardChanged.InvokeAsync(await Api.GetDashboardAsync());
        }
        catch (ApiException exception)
        {
            Snackbar.Add(L["That did not work: {0}", exception.Message], Severity.Error);
        }
    }
}
