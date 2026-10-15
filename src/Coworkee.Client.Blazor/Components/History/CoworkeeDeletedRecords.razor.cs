using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.History;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Auditing;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components;

/// <summary>The soft deleted records of an entity set with history, newest first; each can come back as it was or at an earlier state.</summary>
public partial class CoworkeeDeletedRecords
{
    private IReadOnlyList<DeletedRecordDto>? _records;
    private string? _open;

    [Inject] private IEntityHistoryApi Api { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public string EntitySet { get; set; } = string.Empty;

    [Parameter] public Func<string, string>? FieldLabel { get; set; }

    [Parameter] public EventCallback OnRestored { get; set; }

    protected override async Task OnParametersSetAsync() => await ReloadAsync();

    public async Task ReloadAsync()
    {
        try
        {
            _records = await Api.GetDeletedAsync(EntitySet);
        }
        catch (ApiException exception)
        {
            _records = [];
            Snackbar.Add(SnackbarApiExtensions.Describe(exception), Severity.Error);
        }
    }

    // as it was when deleted: the newest entry is the deletion itself
    private async Task RestoreAsync(DeletedRecordDto record)
    {
        var id = Guid.Parse(record.EntityId);
        EntityHistoryDto history;
        try
        {
            history = await Api.GetAsync(EntitySet, id);
        }
        catch (ApiException exception)
        {
            Snackbar.Add(SnackbarApiExtensions.Describe(exception), Severity.Error);
            return;
        }

        if (history.Entries.Count > 0
            && await EntityHistoryRestoreDialog.ConfirmAsync(Dialogs, Api, Snackbar, L, EntitySet, id, new EntityHistoryRestoreRequest(history.Entries[0].Id),
                FieldLabel ?? (p => CoworkeeEntityHistory.DefaultLabel(L, p))))
        {
            await RestoredAsync();
        }
    }

    private async Task RestoredAsync()
    {
        _open = null;
        await ReloadAsync();
        await OnRestored.InvokeAsync();
    }
}
