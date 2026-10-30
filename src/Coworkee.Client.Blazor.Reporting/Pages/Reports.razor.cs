using System.Text.Json;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Reporting.Api;
using Coworkee.Client.Blazor.Reporting.Components;
using Coworkee.Contracts.Reporting;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using MudBlazor;

namespace Coworkee.Client.Blazor.Reporting.Pages;

public partial class Reports
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private List<ReportSummaryDto>? _reports;
    private DashboardReportDto? _dashboard;
    private string? _search;
    private string _scope = "all";
    private bool _ai;

    [Inject] private IReportsApi Api { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private NavigationManager Nav { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private FileDownloader Downloader { get; set; } = null!;

    [Inject] private Security.PermissionStore Permissions { get; set; } = null!;

    [Inject] private State.ReportDrafts Drafts { get; set; } = null!;

    private IEnumerable<ReportSummaryDto> Shown => (_reports ?? [])
        .Where(r => _scope switch
        {
            "favorites" => r.IsFavorite,
            "mine" => r.IsOwner,
            "shared" => !r.IsOwner,
            _ => true,
        })
        .Where(r => string.IsNullOrWhiteSpace(_search)
            || r.Name.Contains(_search, StringComparison.CurrentCultureIgnoreCase)
            || r.Description?.Contains(_search, StringComparison.CurrentCultureIgnoreCase) == true);

    protected override async Task OnInitializedAsync()
    {
        await ReloadAsync();
        await RunAsync(async () => _dashboard = await Api.GetDashboardAsync());
        _ai = await Permissions.HasAsync(ReportPermissions.Manage) && await Api.IsAiAvailableAsync();
    }

    private async Task ReloadAsync() => _reports = [.. await Api.GetReportsAsync()];

    private void Open(ReportSummaryDto report) => Nav.NavigateTo($"reports/{report.Id}");

    private string Owner(ReportSummaryDto report) => report.IsOwner ? L["by you"] : L["by {0}", report.OwnerName ?? "?"];

    internal static string VisibilityIcon(ReportVisibility visibility) => visibility switch
    {
        ReportVisibility.Organisation => Icons.Material.Outlined.Public,
        ReportVisibility.Roles => Icons.Material.Outlined.Groups,
        ReportVisibility.Groups => Icons.Material.Outlined.AccountTree,
        _ => Icons.Material.Outlined.Lock,
    };

    internal static string VisibilityText(ReportVisibility visibility) => visibility switch
    {
        ReportVisibility.Organisation => "Organisation",
        ReportVisibility.Roles => "Roles",
        ReportVisibility.Groups => "Groups",
        _ => "Private",
    };

    private async Task CreateAsync()
    {
        if (await ReportSettingsDialog.ShowAsync(Dialogs, L["New report"], new ReportSettings { Name = L["New report"] }) is not { } settings)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var report = await Api.CreateAsync(new SaveReportRequest(settings.Name, settings.Description, settings.Icon, settings.Visibility, settings.RoleIds,
                new ReportDefinition { RefreshSeconds = settings.RefreshSeconds }, settings.GroupIds));
            Nav.NavigateTo($"reports/{report.Id}?edit=true");
        });
    }

    // the generated report opens in the designer unsaved
    private async Task CreateWithAiAsync()
    {
        if (await ReportAiDialog.ShowAsync(Dialogs, L["Create with AI"], null) is { } definition)
        {
            Drafts.Set(new SaveReportRequest(L["New report"], null, null, ReportVisibility.Private, [], definition));
            Nav.NavigateTo("reports/new");
        }
    }

    private async Task FavoriteAsync(ReportSummaryDto report) => await RunAsync(async () =>
    {
        await Api.SetFavoriteAsync(report.Id, !report.IsFavorite);
        await ReloadAsync();
    });

    private async Task DuplicateAsync(ReportSummaryDto report) => await RunAsync(async () =>
    {
        var copy = await Api.DuplicateAsync(report.Id);
        Nav.NavigateTo($"reports/{copy.Id}?edit=true");
    });

    private async Task ExportAsync(ReportSummaryDto summary) => await RunAsync(async () =>
    {
        var report = await Api.GetReportAsync(summary.Id);
        await Downloader.DownloadAsync(report.Name + ReportFile.Extension, "application/json",
            JsonSerializer.Serialize(new ReportFile(report.Name, report.Description, report.Icon, report.Definition), Json));
    });

    private async Task DeleteAsync(ReportSummaryDto report)
    {
        if (await Dialogs.ConfirmAsync(L["Delete"], L["Delete {0}? This cannot be undone.", report.Name], L["Delete"], L["Cancel"], Icons.Material.Outlined.DeleteForever))
        {
            await RunAsync(async () =>
            {
                await Api.DeleteAsync(report.Id);
                await ReloadAsync();
            });
        }
    }

    private async Task ImportAsync(IBrowserFile? file)
    {
        if (file is null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            await using var stream = file.OpenReadStream(5 * 1024 * 1024);
            var imported = await JsonSerializer.DeserializeAsync<ReportFile>(stream, Json) ?? throw new JsonException();
            var report = await Api.CreateAsync(new SaveReportRequest(imported.Name, imported.Description, imported.Icon, ReportVisibility.Private, [], imported.Definition));
            Snackbar.Add(L["{0} imported.", report.Name], Severity.Success);
            Nav.NavigateTo($"reports/{report.Id}");
        });
    }

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Errors is { Count: > 0 } errors ? string.Join(" ", errors.SelectMany(e => e.Value)) : L["That did not work: {0}", exception.Message], Severity.Error);
        }
        catch (JsonException)
        {
            Snackbar.Add(L["The file is not a report."], Severity.Error);
        }
    }
}
