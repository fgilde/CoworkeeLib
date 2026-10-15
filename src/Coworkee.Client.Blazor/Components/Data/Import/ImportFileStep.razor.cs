using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Data.Import;

/// <summary>Uploads the file; for text files the detected separator, encoding and header can be corrected.</summary>
public partial class ImportFileStep
{
    private const long MaxBytes = 20 * 1024 * 1024;
    private static readonly string[] Encodings = ["utf-8", "windows-1252", "iso-8859-1", "utf-16"];
    private bool _busy;

    [Parameter, EditorRequired] public ImportWizardState State { get; set; } = null!;

    [Parameter] public EventCallback Changed { get; set; }

    [Inject] private IDataTransferApi Api { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private async Task UploadAsync(IBrowserFile? file)
    {
        if (file is null)
        {
            return;
        }

        if (file.Size > MaxBytes)
        {
            Snackbar.Add(L["At most 20 MB. The first row names the columns."], Severity.Warning);
            return;
        }

        await RunAsync(async () =>
        {
            await using var stream = file.OpenReadStream(MaxBytes);
            State.Apply(await Api.UploadAsync(State.Target.EntitySet, stream, file.Name));
        });
    }

    private Task ReadAgainAsync(ImportParseOptions options) =>
        RunAsync(async () => State.Apply(await Api.ReadAgainAsync(State.Sheet!.JobId, options)));

    private async Task RunAsync(Func<Task> action)
    {
        _busy = true;
        try
        {
            await Snackbar.RunAsync(action);
        }
        finally
        {
            _busy = false;
            await Changed.InvokeAsync();
        }
    }
}
