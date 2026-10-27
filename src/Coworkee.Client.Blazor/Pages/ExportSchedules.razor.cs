using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Components.Exports;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Data;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages;

/// <summary>The user's scheduled list exports: send now, change, delete.</summary>
public partial class ExportSchedules
{
    private List<ExportScheduleDto>? _schedules;

    [Inject] private IDataTransferApi Api { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    protected override Task OnInitializedAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        if (!await Snackbar.RunAsync(async () => _schedules = [.. await Api.GetExportSchedulesAsync()]))
        {
            _schedules = [];
        }
    }

    private async Task RunAsync(ExportScheduleDto schedule)
    {
        ExportScheduleDto? ran = null;
        if (await Snackbar.RunAsync(async () => ran = await Api.RunExportScheduleAsync(schedule.Id)))
        {
            Snackbar.Add(ran!.LastError is { } error ? L["Last mail failed: {0}", error] : L["The export is on its way."], ran.LastError is null ? Severity.Success : Severity.Warning);
            await LoadAsync();
        }
    }

    private async Task EditAsync(ExportScheduleDto schedule)
    {
        if (await ExportScheduleDialog.ShowAsync(Dialogs, L["Schedule export"], schedule.EntitySet, schedule.Query, schedule.Name, schedule) is { } request
            && await Snackbar.RunAsync(() => Api.SaveExportScheduleAsync(schedule.Id, request)))
        {
            await LoadAsync();
        }
    }

    private async Task DeleteAsync(ExportScheduleDto schedule)
    {
        if (await Dialogs.ConfirmAsync(L["Delete"], L["Delete {0}? This cannot be undone.", schedule.Name], L["Delete"], L["Cancel"], Icons.Material.Outlined.Delete)
            && await Snackbar.RunAsync(() => Api.DeleteExportScheduleAsync(schedule.Id)))
        {
            await LoadAsync();
        }
    }
}
