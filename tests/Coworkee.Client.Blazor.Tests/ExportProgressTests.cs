using System.Threading.Channels;
using Bunit;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Contracts.Data;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

/// <summary>The export dialog follows the progress stream of a fake server: rows and percent, the one-time link, a queued export, cancel and errors.</summary>
public sealed class ExportProgressTests : ClientTestBase
{
    private readonly Channel<DataExportProgress> _lines = Channel.CreateUnbounded<DataExportProgress>();
    private readonly Guid _jobId = Guid.CreateVersion7();
    private readonly BunitJSModuleInterop _module;
    private readonly IRenderedComponent<MudDialogProvider> _dialogs;

    public ExportProgressTests()
    {
        AddAuthorization().SetAuthorized("Ada");
        _module = JSInterop.SetupModule("./_content/Coworkee.Client.Blazor/coworkee.js");
        _module.SetupVoid("downloadUrl", _ => true);
        Transfer.StreamExportAsync("Gadgets", "?format=xlsx", Arg.Any<CancellationToken>()).Returns(call => _lines.Reader.ReadAllAsync(call.Arg<CancellationToken>()));
        Transfer.GetJobAsync(_jobId, Arg.Any<CancellationToken>()).Returns(new DataTransferJobDto(
            _jobId, DataTransferKind.Export, "Gadgets", DataTransferStatus.Running, false, 0, 0, 0, 0, 0, null, null, null, DateTimeOffset.UtcNow, null, false));
        _dialogs = Render<MudDialogProvider>();
        Render<MudSnackbarProvider>();
    }

    private IEnumerable<Snackbar> Snackbars => Services.GetRequiredService<ISnackbar>().ShownSnackbars;

    private Task ShowAsync() => _dialogs.InvokeAsync(() => ExportProgressDialog.ShowAsync(Services.GetRequiredService<IDialogService>(), "Export", "Gadgets", "?format=xlsx", "list"));

    private void Write(DataExportProgress line) => _lines.Writer.TryWrite(line with { JobId = _jobId }).ShouldBeTrue();

    [Fact]
    public async Task Rows_and_percent_follow_the_stream_and_the_file_comes_through_the_one_time_link()
    {
        var showing = ShowAsync();
        Write(new DataExportProgress(DataExportProgress.Counting, 0));
        _dialogs.WaitForAssertion(() => _dialogs.Find("[data-testid='export-progress-phase']").TextContent.ShouldBe("Counting rows…"));

        Write(new DataExportProgress(DataExportProgress.Reading, 20) { Rows = 500, Total = 2000 });
        _dialogs.WaitForAssertion(() => _dialogs.Find("[data-testid='export-progress-rows']").TextContent.ShouldBe("500 of 2000 rows"));
        _dialogs.Find("[data-testid='export-progress-percent']").TextContent.ShouldBe("20 %");
        _dialogs.Find("[data-testid='export-progress-phase']").TextContent.ShouldBe("Reading rows…");

        Write(new DataExportProgress(DataExportProgress.Ready, 100, "api/v1/data/downloads/t0ken", "Gadgets.xlsx"));
        await showing;

        _dialogs.WaitForAssertion(() => _module.VerifyInvoke("downloadUrl").Arguments.ShouldBe(["http://localhost/api/v1/data/downloads/t0ken", "list.xlsx"]));
        _dialogs.FindAll("[data-testid='export-progress']").ShouldBeEmpty();
    }

    [Fact]
    public async Task A_queued_export_goes_to_the_background_notice()
    {
        var showing = ShowAsync();
        Write(new DataExportProgress(DataExportProgress.Counting, 0));
        Write(new DataExportProgress(DataExportProgress.Queued, 100));
        await showing;

        _dialogs.WaitForAssertion(() => Snackbars.ShouldContain(s => s.Message == "The export runs in the background. You get the file here when it is ready."));
        _module.Invocations.ShouldNotContain(i => i.Identifier == "downloadUrl");
    }

    [Fact]
    public async Task Cancel_cancels_the_job_on_the_server()
    {
        var showing = ShowAsync();
        Write(new DataExportProgress(DataExportProgress.Reading, 40) { Rows = 1, Total = 2 });
        _dialogs.WaitForAssertion(() => _dialogs.Find("[data-testid='export-progress-rows']").TextContent.ShouldBe("1 of 2 rows"));

        await _dialogs.Find("[data-testid='export-progress-cancel']").ClickAsync(new());
        await showing;

        _dialogs.WaitForAssertion(() => Snackbars.ShouldContain(s => s.Message == "The export was cancelled."));
        await Transfer.Received(1).CancelJobAsync(_jobId, Arg.Any<CancellationToken>());
        await Transfer.DidNotReceive().GetJobAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        Snackbars.ShouldNotContain(s => s.Message.StartsWith("The export runs in the background", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Cancel_before_the_first_line_waits_for_the_job_id_it_carries()
    {
        var showing = ShowAsync();
        _dialogs.WaitForAssertion(() => _dialogs.Find("[data-testid='export-progress-cancel']"));
        await _dialogs.Find("[data-testid='export-progress-cancel']").ClickAsync(new());
        await Transfer.DidNotReceive().CancelJobAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());

        Write(new DataExportProgress(DataExportProgress.Counting, 0));
        await showing;

        _dialogs.WaitForAssertion(() => Transfer.Received(1).CancelJobAsync(_jobId, Arg.Any<CancellationToken>()));
        _dialogs.WaitForAssertion(() => Snackbars.ShouldContain(s => s.Message == "The export was cancelled."));
    }

    [Fact]
    public async Task A_failed_line_shows_its_message()
    {
        var showing = ShowAsync();
        Write(new DataExportProgress(DataExportProgress.Failed, 100, Message: "The export took too long."));
        await showing;

        _dialogs.WaitForAssertion(() => Snackbars.ShouldContain(s => s.Message == "The export failed: The export took too long." && s.Severity == Severity.Error));
    }

    [Theory]
    [InlineData(429, "You have too many exports running. Wait until one is ready.")]
    [InlineData(400, "The export query is not valid.")]
    public async Task Refused_exports_say_why(int status, string message)
    {
        Transfer.StreamExportAsync("Gadgets", "?format=xlsx", Arg.Any<CancellationToken>()).Returns(Refused(status));

        await ShowAsync();

        _dialogs.WaitForAssertion(() => Snackbars.ShouldContain(s => s.Message == message && s.Severity == Severity.Error));
    }

    [Fact]
    public async Task A_timeout_of_the_connection_is_shown()
    {
        Transfer.StreamExportAsync("Gadgets", "?format=xlsx", Arg.Any<CancellationToken>()).Returns(TimedOut());

        await ShowAsync();

        _dialogs.WaitForAssertion(() => Snackbars.ShouldContain(s => s.Message == "The export took too long."));
    }

    private static async IAsyncEnumerable<DataExportProgress> Refused(int status)
    {
        await Task.Yield();
        throw new ApiException(status, null, null);
#pragma warning disable CS0162 // an iterator needs a yield
        yield break;
#pragma warning restore CS0162
    }

    private static async IAsyncEnumerable<DataExportProgress> TimedOut()
    {
        await Task.Yield();
        throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.");
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }
}
