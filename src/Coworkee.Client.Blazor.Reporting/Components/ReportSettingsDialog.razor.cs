using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Reporting.Api;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Reporting;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Reporting.Components;

public partial class ReportSettingsDialog
{
    private static readonly string[] Icons =
    [
        MudBlazor.Icons.Material.Outlined.SpaceDashboard, MudBlazor.Icons.Material.Outlined.Insights, MudBlazor.Icons.Material.Outlined.QueryStats,
        MudBlazor.Icons.Material.Outlined.ShoppingCart, MudBlazor.Icons.Material.Outlined.Inventory2, MudBlazor.Icons.Material.Outlined.Groups,
        MudBlazor.Icons.Material.Outlined.Euro, MudBlazor.Icons.Material.Outlined.LocalShipping, MudBlazor.Icons.Material.Outlined.Speed,
    ];

    private static readonly (int Seconds, string Label)[] Refreshes = [(0, "Never"), (30, "Every 30 seconds"), (60, "Every minute"), (300, "Every 5 minutes"), (900, "Every 15 minutes"), (3600, "Every hour")];

    private IReadOnlyList<ReportRoleDto> _roles = [];

    private IReadOnlyList<ReportGroupDto> _groups = [];

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private IReportsApi Api { get; set; } = null!;

    [Inject] private PermissionStore Permissions { get; set; } = null!;

    [CascadingParameter] private IMudDialogInstance Dialog { get; set; } = null!;

    [Parameter] public ReportSettings Model { get; set; } = new();

    private bool CanShare { get; set; }

    protected override async Task OnInitializedAsync()
    {
        CanShare = await Permissions.HasAsync(ReportPermissions.Share);
        if (CanShare)
        {
            try
            {
                _roles = await Api.GetRolesAsync();
                _groups = await Api.GetGroupsAsync();
            }
            catch (ApiException)
            {
            }
        }
    }

    // a group indents by the depth of its code (00001.00002 is a child of 00001)
    private static string Indent(ReportGroupDto group) => new(' ', 4 * group.Code.Count(c => c == '.'));

    private string GroupNames(IReadOnlyList<string> ids) =>
        string.Join(", ", ids.Select(id => _groups.FirstOrDefault(g => g.Id.ToString() == id)?.Name ?? id));

    public static async Task<ReportSettings?> ShowAsync(IDialogService dialogs, string title, ReportSettings model)
    {
        var dialog = await dialogs.ShowSideSheetAsync<ReportSettingsDialog>(title, new DialogParameters<ReportSettingsDialog> { { d => d.Model, model } });
        var result = await dialog.Result;
        return result is { Canceled: false, Data: ReportSettings settings } ? settings : null;
    }
}

public sealed class ReportSettings
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? Icon { get; set; }

    public int RefreshSeconds { get; set; }

    public ReportVisibility Visibility { get; set; }

    public List<Guid> RoleIds { get; set; } = [];

    public List<Guid> GroupIds { get; set; } = [];
}
