using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Reporting.Api;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Realtime;
using Coworkee.Contracts.Reporting;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Reporting.Components;

/// <summary>
/// The dashboard page's content: the report the user chose as dashboard, else the organisation's, else <see cref="ChildContent"/>
/// (the app's own dashboard) or a hint to choose one. Follows changes of the choice in other tabs.
/// </summary>
public partial class ReportDashboard
{
    private static readonly string Topic = RealtimeTopics.Type("DashboardReport");

    private bool _loaded;
    private bool _canView;
    private Guid? _reportId;

    [Inject] private IReportsApi Api { get; set; } = null!;

    [Inject] private PermissionStore Permissions { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    /// <summary>Shown while no report is the dashboard, e.g. the app's dashboard without reports.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    protected override async Task OnInitializedAsync()
    {
        _canView = await Permissions.HasAsync(ReportPermissions.View);
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            _reportId = _canView ? (await Api.GetDashboardAsync())?.ReportId : null;
        }
        catch (ApiException)
        {
            _reportId = null;
        }

        _loaded = true;
        await InvokeAsync(StateHasChanged);
    }
}
