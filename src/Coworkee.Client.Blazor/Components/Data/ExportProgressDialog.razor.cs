using System.Net;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Data;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Extensions;
using MudBlazor.Extensions.Options;

namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>
/// Runs a list export over the progress stream: shows the rows and percent, downloads the file through its one-time link, hands an export the
/// server queued to <see cref="DataTransferWatcher"/>, and shows errors. Cancel stops waiting; the server finishes the file and it comes like a background export.
/// </summary>
public partial class ExportProgressDialog : IDisposable
{
    private readonly CancellationTokenSource _cancel = new();
    private DataExportProgress? _line;
    private Guid? _jobId;
    private bool _closed;

    [Inject] private IDataTransferApi Transfer { get; set; } = null!;

    [Inject] private DataTransferWatcher Watcher { get; set; } = null!;

    [Inject] private FileDownloader Downloader { get; set; } = null!;

    [Inject] private NavigationManager Navigation { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [CascadingParameter] private IMudDialogInstance Dialog { get; set; } = null!;

    [Parameter, EditorRequired] public string EntitySet { get; set; } = string.Empty;

    /// <summary>The export's query string (<see cref="ExportQueries.Of"/>).</summary>
    [Parameter, EditorRequired] public string Query { get; set; } = string.Empty;

    /// <summary>The name of the downloaded file without extension; the server's name when empty.</summary>
    [Parameter] public string? FileName { get; set; }

    private string PhaseText => _line?.Phase switch
    {
        DataExportProgress.Reading => L["Reading rows…"],
        DataExportProgress.Writing => L["Writing the file…"],
        _ => L["Counting rows…"],
    };

    private string RowsText => _line is { Rows: { } rows, Total: { } total } ? L["{0} of {1} rows", rows, total] : string.Empty;

    public static async Task ShowAsync(IDialogService dialogs, string title, string entitySet, string query, string? fileName)
    {
        var dialog = await dialogs.ShowExAsync<ExportProgressDialog>(title, new DialogParameters<ExportProgressDialog>
        {
            { d => d.EntitySet, entitySet }, { d => d.Query, query }, { d => d.FileName, fileName },
        }, new DialogOptionsEx { MaxWidth = MaxWidth.ExtraSmall, FullWidth = true, BackdropClick = false, CloseOnEscapeKey = false, CloseButton = false });
        await dialog.Result;
    }

    protected override async Task OnInitializedAsync()
    {
        await Task.Yield();
        try
        {
            await foreach (var line in Transfer.StreamExportAsync(EntitySet, Query, _cancel.Token))
            {
                (_line, _jobId) = (line, line.JobId ?? _jobId);
                if (line.Phase is DataExportProgress.Ready or DataExportProgress.Queued or DataExportProgress.Failed)
                {
                    await FinishAsync(line);
                    return;
                }

                StateHasChanged();
            }
        }
        catch (OperationCanceledException) when (_cancel.IsCancellationRequested)
        {
            // the user stopped waiting: the server writes the file all the same, so it is followed like a background export
            await AnnounceAsync(_jobId);
        }
        catch (OperationCanceledException)
        {
            Snackbar.Add(L["The export took too long."], Severity.Error);
        }
        catch (ApiException exception)
        {
            Snackbar.Add(Describe(exception), Severity.Error);
        }
        catch (HttpRequestException)
        {
            Snackbar.Add(L["The connection to the server was lost."], Severity.Error);
        }

        Close();
    }

    private async Task FinishAsync(DataExportProgress line)
    {
        Close();
        switch (line.Phase)
        {
            case DataExportProgress.Ready when line.Url is { } url:
                var name = string.IsNullOrWhiteSpace(FileName) ? line.FileName ?? EntitySet : FileName + Path.GetExtension(line.FileName);
                await Downloader.DownloadUrlAsync(Navigation.ToAbsoluteUri(url).ToString(), name);
                break;
            case DataExportProgress.Queued:
                await AnnounceAsync(line.JobId);
                break;
            default:
                Snackbar.Add(L["The export failed: {0}", L[line.Message ?? "Export failed"]], Severity.Error);
                break;
        }
    }

    private async Task AnnounceAsync(Guid? jobId)
    {
        Close();
        if (jobId is { } id)
        {
            await Snackbar.RunAsync(async () => await Watcher.Announce(await Transfer.GetJobAsync(id)));
        }
    }

    private string Describe(ApiException exception) => (HttpStatusCode)exception.Status switch
    {
        HttpStatusCode.TooManyRequests => L["You have too many exports running. Wait until one is ready."],
        HttpStatusCode.BadRequest => L["The export query is not valid."],
        HttpStatusCode.Forbidden => L["You may not export this list."],
        _ => SnackbarApiExtensions.Describe(exception),
    };

    private void Cancel() => _cancel.Cancel();

    private void Close()
    {
        if (!_closed)
        {
            _closed = true;
            Dialog.Close();
        }
    }

    public void Dispose()
    {
        _cancel.Cancel();
        _cancel.Dispose();
        GC.SuppressFinalize(this);
    }
}
