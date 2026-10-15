using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Data;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Data.Import;

/// <summary>The first rows as the import reads them, each with what it would do and the messages of the validation.</summary>
public partial class ImportPreviewStep
{
    private bool _onlyErrors;

    [Parameter, EditorRequired] public ImportWizardState State { get; set; } = null!;

    [Parameter] public EventCallback Changed { get; set; }

    [Inject] private IDataTransferApi Api { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private IEnumerable<ImportFieldDto> Fields => State.Target.Fields.Where(f => State.Columns.Contains(f.Name));

    protected override async Task OnInitializedAsync()
    {
        if (State.Preview is null)
        {
            await Snackbar.RunAsync(async () => State.Preview = await Api.PreviewAsync(State.Sheet!.JobId, State.Settings()));
            _onlyErrors = State.Preview?.Invalid.Count > 0;
            await Changed.InvokeAsync();
        }
    }

    private static string Icon(ImportPreviewRowDto row) => row.Errors.Count > 0 ? Icons.Material.Outlined.ErrorOutline
        : row.Action == "Update" ? Icons.Material.Outlined.Edit
        : Icons.Material.Outlined.Add;
}
