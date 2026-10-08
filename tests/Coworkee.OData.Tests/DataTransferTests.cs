using System.Net;
using System.Net.Http.Json;
using ClosedXML.Excel;
using Coworkee.Contracts.Data;
using Coworkee.Contracts.Identity;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.OData.Tests;

public sealed class DataTransferTests(ODataApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        await app.InDbAsync(_setup.TenantId, async db =>
        {
            db.AddRange(
                new Gadget { Name = "Drill", Category = "Tools", Price = 99.5m, SerialCode = "S-1", TenantId = _setup.TenantId },
                new Gadget { Name = "Hammer", Category = "Tools", Price = 19, TenantId = _setup.TenantId },
                new Gadget { Name = "Lamp", Category = "Light", Price = 35, TenantId = _setup.TenantId },
                new Gadget { Name = "Vault", Category = "Secret", Price = 1000, TenantId = _setup.TenantId });
            return await db.SaveChangesAsync();
        });
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task Export_writes_the_filtered_and_sorted_rows_without_hidden_columns()
    {
        var response = await Admin.GetAsync("/api/v1/data/Gadgets/export?$filter=Category eq 'Tools'&$orderby=Price desc", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var workbook = new XLWorkbook(await response.Content.ReadAsStreamAsync(Ct));
        var sheet = workbook.Worksheet(1);
        var headers = sheet.Row(1).CellsUsed().Select(c => c.GetString()).ToList();
        headers.ShouldContain("Name");
        headers.ShouldContain("Price");
        headers.ShouldNotContain("SerialCode");
        var name = headers.IndexOf("Name") + 1;
        sheet.RowsUsed().Skip(1).Select(r => r.Cell(name).GetString()).ShouldBe(["Drill", "Hammer"]);
        sheet.Cell(2, headers.IndexOf("Price") + 1).GetDouble().ShouldBe(99.5);
    }

    [Fact]
    public async Task Export_without_options_writes_every_visible_row()
    {
        using var workbook = new XLWorkbook(await Admin.GetStreamAsync("/api/v1/data/Gadgets/export", Ct));

        workbook.Worksheet(1).RowsUsed().Count().ShouldBe(4);
    }

    [Fact]
    public async Task Import_sends_one_command_per_row_and_reports_failed_rows()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Gadgets");
        sheet.Cell(1, 1).Value = "Name";
        sheet.Cell(1, 2).Value = "category";
        sheet.Cell(1, 3).Value = "Price";
        sheet.Cell(1, 4).Value = "Ignored";
        sheet.Cell(2, 1).Value = "Saw";
        sheet.Cell(2, 2).Value = "Tools";
        sheet.Cell(2, 3).Value = 12.5;
        sheet.Cell(3, 2).Value = "Tools";
        sheet.Cell(4, 1).Value = 4711;
        sheet.Cell(4, 2).Value = "Light";
        sheet.Cell(4, 3).Value = "7";
        sheet.Cell(4, 4).Value = "x";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        using var form = new MultipartFormDataContent { { new ByteArrayContent(stream.ToArray()), "file", "gadgets.xlsx" } };

        var result = await (await Admin.PostAsync("/api/v1/data/Gadgets/import", form, Ct)).Content.ReadFromJsonAsync<ImportResult>(Ct);

        result!.Imported.ShouldBe(2);
        result.Errors.Single().Row.ShouldBe(3);
        result.Errors.Single().Message.ShouldContain("Name");
        var added = await app.InDbAsync(_setup.TenantId, db => db.Set<Gadget>().Where(g => g.Name == "Saw" || g.Name == "4711").OrderBy(g => g.Name).ToListAsync(Ct));
        added.Select(g => (g.Name, g.Category, g.Price)).ShouldBe([("4711", "Light", 7m), ("Saw", "Tools", 12.5m)]);
    }

    [Fact]
    public async Task Unknown_sets_are_not_found_and_export_needs_the_permission()
    {
        (await Admin.GetAsync("/api/v1/data/Nothing/export", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var email = $"viewer-{Guid.NewGuid():N}@acme.test";
        var user = await (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null), Ct)).Content.ReadFromJsonAsync<UserDto>(Ct);

        (await app.As(user!.Id, _setup.TenantId).GetAsync("/api/v1/data/Gadgets/export", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
