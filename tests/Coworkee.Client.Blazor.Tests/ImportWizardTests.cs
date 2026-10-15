using System.Security.Claims;
using Bunit;
using Coworkee.Client.Blazor.Components.Data.Import;
using Coworkee.Contracts.Data;
using Coworkee.Contracts.Realtime;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class ImportWizardTests : ClientTestBase
{
    private static readonly Guid Me = Guid.CreateVersion7();
    private static readonly Guid Upload = Guid.CreateVersion7();

    private static readonly ImportTargetDto Target = new(
        "Widgets",
        [
            new ImportFieldDto("Id", "Id", "Guid", false, KeyOnly: true),
            new ImportFieldDto("Name", "Name", "String", true),
            new ImportFieldDto("Price", "Price", "Decimal", false),
            new ImportFieldDto("Color", "Color", "String", true),
        ],
        CanUpdate: true,
        CanUndo: true,
        KeyFields: ["Id", "Name"]);

    private static readonly ImportSheetDto Sheet = new(Upload, "widgets.csv", 2, ";", "utf-8", true,
    [
        new ImportColumnDto(0, "Name", ["Bolt", "Nut"], "Name"),
        new ImportColumnDto(1, "Preis", ["1,5", "x"], "Price"),
        new ImportColumnDto(2, "Note", ["a", "b"], null),
    ]);

    public ImportWizardTests()
    {
        AddAuthorization().SetAuthorized("Ada").SetClaims(new Claim("sub", Me.ToString()));
        Render<MudPopoverProvider>();
        Render<MudSnackbarProvider>();
    }

    [Fact]
    public void The_state_keeps_each_field_on_one_column_and_applies_profiles_by_header()
    {
        var state = new ImportWizardState(Target);
        state.Apply(Sheet);

        state.Columns.ShouldBe(["Name", "Price", null]);
        state.SetColumn(2, "Name");
        state.Columns.ShouldBe([null, "Price", "Name"]);
        state.MissingRequired.Select(f => f.Name).ShouldBe(["Color"]);

        state.Apply(new ImportProfileDto(Guid.Empty, "Widgets", "Supplier", new Dictionary<string, string> { ["Note"] = "Color", ["Preis"] = "Price", ["Gone"] = "Name" },
            ImportMode.Upsert, "Name", "de-DE"));

        state.Columns.ShouldBe([null, "Price", "Color"]);
        state.Settings().ShouldBe(new ImportSettings(state.Columns, ImportMode.Upsert, "Name", "de-DE", new ImportParseOptions(";", "utf-8", true)));
        state.Profile("Again").Columns.ShouldBe(new Dictionary<string, string> { ["Preis"] = "Price", ["Note"] = "Color" });
    }

    [Fact]
    public void The_file_step_uploads_and_reads_text_files_again_with_another_separator()
    {
        Transfer.UploadAsync("Widgets", Arg.Any<Stream>(), "widgets.csv", Arg.Any<CancellationToken>()).Returns(Sheet);
        Transfer.ReadAgainAsync(Upload, Arg.Any<ImportParseOptions>(), Arg.Any<CancellationToken>()).Returns(Sheet with { Delimiter = ",", RowCount = 7 });
        var state = new ImportWizardState(Target);
        var step = Render<ImportFileStep>(p => p.Add(s => s.State, state));

        step.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("Name;Preis\nBolt;1,5", "widgets.csv"));

        step.WaitForAssertion(() => step.Find("[data-testid='import-sheet']").TextContent.ShouldContain("widgets.csv: 2 rows, 3 columns"));
        state.Columns.ShouldBe(["Name", "Price", null]);
        step.InvokeAsync(() => step.FindComponents<MudSelect<string>>()[0].Instance.ValueChanged.InvokeAsync(","));
        step.WaitForAssertion(() => state.Sheet!.RowCount.ShouldBe(7));
        Transfer.Received(1).ReadAgainAsync(Upload, new ImportParseOptions(",", "utf-8", true), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_mapping_step_shows_suggestions_asks_for_a_key_and_saves_the_mapping()
    {
        Transfer.GetProfilesAsync("Widgets", Arg.Any<CancellationToken>()).Returns([]);
        var state = new ImportWizardState(Target);
        state.Apply(Sheet);
        var step = Render<ImportMappingStep>(p => p.Add(s => s.State, state));

        step.WaitForAssertion(() => step.Find("[data-testid='import-missing']").TextContent.ShouldContain("Color"));
        step.FindAll("[data-testid='import-key']").ShouldBeEmpty();

        await step.InvokeAsync(() => step.FindComponent<MudRadioGroup<ImportMode>>().Instance.ValueChanged.InvokeAsync(ImportMode.Upsert));
        step.WaitForAssertion(() => step.Find("[data-testid='import-key']"));
        state.KeyField.ShouldBe("Name");

        step.Find("input[data-testid='import-profile-name'], [data-testid='import-profile-name'] input").Input("Supplier");
        await step.Find("[data-testid='import-profile-save']").ClickAsync(new());
        await Transfer.Received(1).SaveProfileAsync("Widgets",
            Arg.Is<SaveImportProfileRequest>(r => r.Name == "Supplier" && r.Mode == ImportMode.Upsert && r.KeyField == "Name" && r.Columns.Count == 2 && r.Columns["Preis"] == "Price"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void The_check_step_previews_the_rows_and_starts_with_the_failing_ones()
    {
        var invalid = new ImportPreviewRowDto(3, "Create", new Dictionary<string, string?> { ["Name"] = "Nut", ["Price"] = "x" }, ["Price: \"x\" is no valid value."]);
        Transfer.PreviewAsync(Upload, Arg.Any<ImportSettings>(), Arg.Any<CancellationToken>()).Returns(new ImportPreviewDto(2, 2, 1, 1, 0,
            [new ImportPreviewRowDto(2, "Create", new Dictionary<string, string?> { ["Name"] = "Bolt", ["Price"] = "1,5" }, []), invalid], [invalid]));
        var state = new ImportWizardState(Target);
        state.Apply(Sheet);
        var step = Render<ImportPreviewStep>(p => p.Add(s => s.State, state));

        step.WaitForAssertion(() => step.Find("[data-testid='import-counts']").TextContent.ShouldContain("1 with errors"));
        var rows = step.FindAll("[data-testid='import-preview'] tbody tr");
        rows.Single().TextContent.ShouldContain("is no valid value");
        state.Preview!.Valid.ShouldBe(1);
    }

    [Fact]
    public async Task The_import_step_follows_the_job_live_and_undoes_it_after_confirmation()
    {
        var running = new DataTransferJobDto(Guid.CreateVersion7(), DataTransferKind.Import, "Widgets", DataTransferStatus.Running, false, 2, 1, 1, 0, 0, "widgets.csv",
            null, null, DateTimeOffset.UtcNow, null, false);
        var done = running with { Status = DataTransferStatus.Completed, Processed = 2, Failed = 1, ResultFileName = "widgets-errors.csv", CanUndo = true };
        Transfer.StartAsync(Upload, Arg.Any<ImportSettings>(), Arg.Any<CancellationToken>()).Returns(running);
        Transfer.GetJobAsync(running.Id, Arg.Any<CancellationToken>()).Returns(done);
        Transfer.UndoAsync(running.Id, Arg.Any<CancellationToken>()).Returns(done with { Status = DataTransferStatus.Undone, CanUndo = false });
        var dialogs = Render<MudDialogProvider>();
        var state = new ImportWizardState(Target);
        state.Apply(Sheet);
        var step = Render<ImportRunStep>(p => p.Add(s => s.State, state));

        await step.Find("[data-testid='import-start']").ClickAsync(new());
        step.WaitForAssertion(() => step.Find("[data-testid='import-result']").TextContent.ShouldContain("1 of 2 rows"));
        Services.GetRequiredService<FakeRealtimeConnection>().Push(RealtimeTopics.User(Me), RealtimeEventTypes.EntityChanged,
            new EntityChangedPayload("DataTransferJob", running.Id.ToString(), EntityChangedPayload.Updated, ["Processed"]));

        step.WaitForAssertion(() => step.Find("[data-testid='import-status']").TextContent.ShouldBe("Import finished"));
        step.Find("[data-testid='import-errors']");
        var undoing = step.Find("[data-testid='import-undo']").ClickAsync(new());
        dialogs.WaitForAssertion(() => dialogs.FindAll("button").Any(b => b.TextContent.Trim() == "Undo import").ShouldBeTrue());
        await dialogs.FindAll("button").Last(b => b.TextContent.Trim() == "Undo import").ClickAsync(new());
        await undoing;

        step.WaitForAssertion(() => step.Find("[data-testid='import-status']").TextContent.ShouldBe("Import undone"));
        await Transfer.Received(1).StartAsync(Upload, Arg.Is<ImportSettings>(s => !s.DryRun && s.Columns.SequenceEqual(new[] { "Name", "Price", null })), Arg.Any<CancellationToken>());
    }
}
