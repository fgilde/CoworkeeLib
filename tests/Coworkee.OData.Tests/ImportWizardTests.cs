using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Coworkee.Contracts.Data;
using Coworkee.Contracts.Identity;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.OData.Tests;

public sealed class ImportWizardTests(ODataApp app) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        await app.InDbAsync(_setup.TenantId, async db =>
        {
            db.Add(new Widget { Name = "Anvil", Kind = WidgetKind.Wrench, Color = "Red", Price = 10, Active = true, TenantId = _setup.TenantId });
            return await db.SaveChangesAsync();
        });
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    private const string GermanCsv = "Name;Art;Aktiv;Preis;Gemacht\r\nBolt;Claw hammer;x;1.234,50;15.01.2026\r\n;Wrench;;5;\r\nClamp;Nothing;yes;-3;\r\n";

    [Fact]
    public async Task Targets_list_the_importable_sets_with_their_fields()
    {
        var targets = await Admin.GetFromJsonAsync<List<ImportTargetDto>>("/api/v1/data/imports", Json, Ct);

        targets!.Select(t => t.EntitySet).ShouldBe(["Groups", "Gadgets", "Widgets", "Parts", "Crates", "Coupons"], ignoreOrder: true);
        var widgets = targets.Single(t => t.EntitySet == "Widgets");
        widgets.CanUpdate.ShouldBeTrue();
        widgets.CanUndo.ShouldBeTrue();
        widgets.KeyFields.ShouldContain("Name");
        widgets.Fields.Single(f => f.Name == "Kind").Choices.ShouldBe(Enum.GetNames<WidgetKind>());
        widgets.Fields.Single(f => f.Name == "Id").KeyOnly.ShouldBeTrue();
        targets.Single(t => t.EntitySet == "Parts").Fields.Single(f => f.Name == "WidgetId").LookupSet.ShouldBe("Widgets");
        targets.Single(t => t.EntitySet == "Gadgets").CanUpdate.ShouldBeFalse();
    }

    [Fact]
    public async Task Upload_detects_the_file_and_suggests_fields_by_name()
    {
        var sheet = await UploadAsync("Widgets", "widgets.csv", Encoding.UTF8.GetBytes("name;Kind;Color;Unknown\r\nBolt;Wrench;Blue;x\r\n"));

        sheet.Delimiter.ShouldBe(";");
        sheet.HasHeader.ShouldBeTrue();
        sheet.RowCount.ShouldBe(1);
        sheet.Columns.Select(c => c.SuggestedField).ShouldBe(["Name", "Kind", "Color", null]);
        sheet.Columns[0].Samples.ShouldBe(["Bolt"]);

        var again = await PostAsync<ImportSheetDto>($"/api/v1/data/imports/{sheet.JobId}/sheet", new ImportParseOptions(Delimiter: ",", HasHeader: false));
        again.Columns.Count.ShouldBe(1);
        again.RowCount.ShouldBe(2);
    }

    [Fact]
    public async Task Preview_converts_with_the_culture_and_lists_the_failing_rows()
    {
        var sheet = await UploadAsync("Widgets", "widgets.csv", Encoding.UTF8.GetBytes(GermanCsv));

        var preview = await PostAsync<ImportPreviewDto>($"/api/v1/data/imports/{sheet.JobId}/preview", Settings(culture: "de-DE"));

        preview.Total.ShouldBe(3);
        preview.Valid.ShouldBe(1);
        preview.Creates.ShouldBe(1);
        preview.Rows.First().Values["Name"].ShouldBe("Bolt");
        preview.Invalid.Select(r => r.Row).ShouldBe([3, 4]);
        preview.Invalid[0].Errors.ShouldContain(e => e.StartsWith("Name"));
        preview.Invalid[1].Errors.Single().ShouldContain("Nothing");
    }

    [Fact]
    public async Task Import_runs_the_commands_reports_failures_as_a_file_and_can_be_undone()
    {
        var sheet = await UploadAsync("Widgets", "widgets.csv", Encoding.UTF8.GetBytes(GermanCsv));

        var job = await PostAsync<DataTransferJobDto>($"/api/v1/data/imports/{sheet.JobId}/start", Settings(culture: "de-DE"));

        job.Status.ShouldBe(DataTransferStatus.Completed);
        (job.Total, job.Processed, job.Created, job.Failed).ShouldBe((3, 3, 1, 2));
        job.CanUndo.ShouldBeTrue();
        var bolt = await app.InDbAsync(_setup.TenantId, db => db.Set<Widget>().SingleAsync(w => w.Name == "Bolt", Ct));
        (bolt.Kind, bolt.Active, bolt.Price, bolt.MadeAt).ShouldBe((WidgetKind.Hammer, true, 1234.5m, new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero)));

        var errors = Encoding.UTF8.GetString(await Admin.GetByteArrayAsync($"/api/v1/data/jobs/{job.Id}/file", Ct));
        errors.ShouldContain("Name;Art;Aktiv;Preis;Gemacht;Row;Error");
        errors.ShouldContain("Clamp;Nothing;yes;-3;;4;");

        var undone = await PostAsync<DataTransferJobDto>($"/api/v1/data/imports/{job.Id}/undo", new { });
        undone.Status.ShouldBe(DataTransferStatus.Undone);
        (await app.InDbAsync(_setup.TenantId, db => db.Set<Widget>().AnyAsync(w => w.Name == "Bolt", Ct))).ShouldBeFalse();
        (await Admin.PostAsync($"/api/v1/data/imports/{job.Id}/undo", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Upsert_by_name_updates_found_rows_keeping_unmapped_values_and_creates_the_rest()
    {
        var sheet = await UploadAsync("Widgets", "widgets.csv", Encoding.UTF8.GetBytes("Name;Price\r\nAnvil;12\r\nHammerhead;3\r\n"));

        var job = await PostAsync<DataTransferJobDto>($"/api/v1/data/imports/{sheet.JobId}/start", new ImportSettings(["Name", "Price"], ImportMode.Upsert, "Name"));

        (job.Created, job.Updated, job.Failed).ShouldBe((1, 1, 0));
        var anvil = await app.InDbAsync(_setup.TenantId, db => db.Set<Widget>().SingleAsync(w => w.Name == "Anvil", Ct));
        (anvil.Price, anvil.Color, anvil.Kind, anvil.Active).ShouldBe((12m, "Red", WidgetKind.Wrench, true));
    }

    [Fact]
    public async Task Upsert_updates_rows_of_a_set_keyed_by_a_guid_not_named_id()
    {
        var gold = await app.InDbAsync(_setup.TenantId, async db =>
        {
            var coupon = new Coupon { Name = "Gold", Percent = 5, TenantId = _setup.TenantId };
            db.Add(coupon);
            await db.SaveChangesAsync(Ct);
            return coupon.Code;
        });
        var sheet = await UploadAsync("Coupons", "coupons.csv", Encoding.UTF8.GetBytes("Name;Percent\r\nGold;20\r\nSilver;10\r\n"));

        var job = await PostAsync<DataTransferJobDto>($"/api/v1/data/imports/{sheet.JobId}/start", new ImportSettings(["Name", "Percent"], ImportMode.Upsert, "Name"));

        (job.Created, job.Updated, job.Failed).ShouldBe((1, 1, 0));
        (await app.InDbAsync(_setup.TenantId, db => db.Set<Coupon>().SingleAsync(c => c.Code == gold, Ct))).Percent.ShouldBe(20);
    }

    [Fact]
    public async Task Update_mode_fails_rows_without_a_match_and_needs_a_key()
    {
        var sheet = await UploadAsync("Widgets", "widgets.csv", Encoding.UTF8.GetBytes("Name;Price\r\nAnvil;12\r\nGhost;3\r\n"));

        var preview = await PostAsync<ImportPreviewDto>($"/api/v1/data/imports/{sheet.JobId}/preview", new ImportSettings(["Name", "Price"], ImportMode.Update, "Name"));
        var withoutKey = await Admin.PostAsJsonAsync($"/api/v1/data/imports/{sheet.JobId}/preview", new ImportSettings(["Name", "Price"], ImportMode.Update), Json, Ct);

        (preview.Updates, preview.Valid).ShouldBe((1, 1));
        preview.Invalid.Single().Errors.Single().ShouldContain("Ghost");
        withoutKey.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_dry_run_writes_nothing_and_keeps_the_upload_for_the_real_import()
    {
        var sheet = await UploadAsync("Widgets", "widgets.json", Encoding.UTF8.GetBytes("""[{"Name":"Bolt","Color":"Blue"},{"Name":"","Color":"Red"}]"""));
        var settings = new ImportSettings(["Name", "Color"], DryRun: true);

        var dry = await PostAsync<DataTransferJobDto>($"/api/v1/data/imports/{sheet.JobId}/start", settings);
        (dry.DryRun, dry.Created, dry.Failed, dry.CanUndo).ShouldBe((true, 1, 1, false));
        (await app.InDbAsync(_setup.TenantId, db => db.Set<Widget>().CountAsync(Ct))).ShouldBe(1);

        var real = await PostAsync<DataTransferJobDto>($"/api/v1/data/imports/{sheet.JobId}/start", settings with { DryRun = false });
        real.Created.ShouldBe(1);
        (await app.InDbAsync(_setup.TenantId, db => db.Set<Widget>().CountAsync(Ct))).ShouldBe(2);
    }

    [Fact]
    public async Task References_are_found_by_their_display_text()
    {
        var sheet = await UploadAsync("Parts", "parts.csv", Encoding.UTF8.GetBytes("Name;Quantity;Widget\r\nHandle;2;Anvil\r\nBlade;1;Nowhere\r\n"));

        var job = await PostAsync<DataTransferJobDto>($"/api/v1/data/imports/{sheet.JobId}/start", new ImportSettings(["Name", "Quantity", "WidgetId"]));

        (job.Created, job.Failed).ShouldBe((1, 1));
        var anvil = await app.InDbAsync(_setup.TenantId, db => db.Set<Widget>().SingleAsync(w => w.Name == "Anvil", Ct));
        (await app.InDbAsync(_setup.TenantId, db => db.Set<Part>().SingleAsync(Ct))).WidgetId.ShouldBe(anvil.Id);
    }

    [Fact]
    public async Task Profiles_save_by_name_per_set()
    {
        var saved = await PostAsync<ImportProfileDto>("/api/v1/data/Widgets/import/profiles",
            new SaveImportProfileRequest("Supplier", new Dictionary<string, string> { ["Art"] = "Kind" }, ImportMode.Upsert, "Name", "de-DE"));
        await PostAsync<ImportProfileDto>("/api/v1/data/widgets/import/profiles",
            new SaveImportProfileRequest("Supplier", new Dictionary<string, string> { ["Preis"] = "Price" }, ImportMode.Create, null, null));

        var profiles = await Admin.GetFromJsonAsync<List<ImportProfileDto>>("/api/v1/data/Widgets/import/profiles", Json, Ct);
        profiles!.Single().Columns.ShouldBe(new Dictionary<string, string> { ["Preis"] = "Price" });
        (await Admin.DeleteAsync($"/api/v1/data/import-profiles/{saved.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Admin.GetFromJsonAsync<List<ImportProfileDto>>("/api/v1/data/Widgets/import/profiles", Json, Ct))!.ShouldBeEmpty();
    }

    [Fact]
    public async Task Users_without_the_permission_cannot_import_and_see_only_their_own_jobs()
    {
        var sheet = await UploadAsync("Widgets", "widgets.csv", Encoding.UTF8.GetBytes("Name\r\nBolt\r\n"));
        var job = await PostAsync<DataTransferJobDto>($"/api/v1/data/imports/{sheet.JobId}/start", new ImportSettings(["Name"]));
        var email = $"viewer-{Guid.NewGuid():N}@acme.test";
        var user = await (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null), Ct)).Content.ReadFromJsonAsync<UserDto>(Ct);
        var viewer = app.As(user!.Id, _setup.TenantId);

        using var form = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes("Name\r\nX\r\n")), "file", "w.csv" } };
        (await viewer.PostAsync("/api/v1/data/Widgets/import/file", form, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await viewer.GetFromJsonAsync<List<ImportTargetDto>>("/api/v1/data/imports", Json, Ct))!.ShouldBeEmpty();
        (await viewer.GetAsync($"/api/v1/data/jobs/{job.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await viewer.PostAsJsonAsync($"/api/v1/data/imports/{sheet.JobId}/preview", new ImportSettings(["Name"]), Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Exports_take_columns_and_format_and_large_ones_run_as_a_job()
    {
        var csv = Encoding.UTF8.GetString(await Admin.GetByteArrayAsync("/api/v1/data/Widgets/export?format=csv&columns=Name,Price", Ct));
        csv.TrimStart('﻿').ShouldMatch(@"^Name;Price\r\nAnvil;10(\.00)?\r\n$"); // SQL Server keeps the scale of the decimal column

        await app.InDbAsync(_setup.TenantId, async db =>
        {
            db.AddRange(Enumerable.Range(1, 5).Select(i => new Widget { Name = $"W{i}", Color = "Blue", TenantId = _setup.TenantId }));
            return await db.SaveChangesAsync();
        });
        var response = await Admin.GetAsync("/api/v1/data/Widgets/export?format=json&$orderby=Name", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var job = (await response.Content.ReadFromJsonAsync<DataTransferJobDto>(Json, Ct))!;
        var done = await Admin.GetFromJsonAsync<DataTransferJobDto>($"/api/v1/data/jobs/{job.Id}", Json, Ct);
        (done!.Kind, done.Status, done.ResultFileName).ShouldBe((DataTransferKind.Export, DataTransferStatus.Completed, "Widgets.json"));
        var rows = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(await Admin.GetByteArrayAsync($"/api/v1/data/jobs/{job.Id}/file", Ct), Json)!;
        rows.Select(r => r["Name"].GetString()).ShouldBe(["Anvil", "W1", "W2", "W3", "W4", "W5"]);
        (await Admin.GetFromJsonAsync<List<DataTransferJobDto>>("/api/v1/data/jobs", Json, Ct))!.ShouldContain(j => j.Id == job.Id);
    }

    private static ImportSettings Settings(string? culture = null) => new(["Name", "Kind", "Active", "Price", "MadeAt"], Culture: culture);

    private async Task<ImportSheetDto> UploadAsync(string set, string name, byte[] content)
    {
        using var form = new MultipartFormDataContent { { new ByteArrayContent(content), "file", name } };
        var response = await Admin.PostAsync($"/api/v1/data/{set}/import/file", form, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<ImportSheetDto>(Json, Ct))!;
    }

    private async Task<T> PostAsync<T>(string url, object body)
    {
        var response = await Admin.PostAsJsonAsync(url, body, Json, Ct);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<T>(Json, Ct))!;
    }
}
