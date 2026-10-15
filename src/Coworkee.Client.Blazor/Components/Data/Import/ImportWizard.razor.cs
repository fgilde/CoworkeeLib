using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Data;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Data.Import;

/// <summary>
/// Four steps from a file to rows: upload, map the columns, check the rows, import (or a dry run first). Closes with <c>true</c>
/// when rows were written, so the caller reloads.
/// </summary>
public partial class ImportWizard
{
    private static readonly string[] Steps = ["File", "Columns", "Check", "Import"];

    private ImportWizardState? _state;
    private int _step;

    [CascadingParameter] private IMudDialogInstance Dialog { get; set; } = null!;

    [Parameter, EditorRequired] public string EntitySet { get; set; } = string.Empty;

    [Inject] private IDataTransferApi Api { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private bool CanContinue => _state is not null && _step switch
    {
        0 => _state.Sheet is { RowCount: > 0 },
        1 => _state.Columns.Any(c => c is not null) && (_state.Mode == ImportMode.Create || _state.KeyField is not null && _state.Columns.Contains(_state.KeyField)),
        2 => _state.Preview is { Valid: > 0 },
        _ => false,
    };

    protected override async Task OnInitializedAsync()
    {
        ImportTargetDto? target = null;
        if (await Snackbar.RunAsync(async () => target = await Api.GetTargetAsync(EntitySet)))
        {
            _state = new ImportWizardState(target!);
        }
        else
        {
            Dialog.Cancel();
        }
    }

    private void Next() => _step++;

    private void Back()
    {
        _step--;
        _state!.Job = null;
    }

    private void Close()
    {
        if (_state?.Job is { DryRun: false })
        {
            Dialog.Close(DialogResult.Ok(true));
        }
        else
        {
            Dialog.Cancel();
        }
    }
}
