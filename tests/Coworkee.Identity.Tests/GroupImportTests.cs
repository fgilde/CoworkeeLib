using System.Net.Http.Json;
using System.Text;
using Coworkee.Contracts.Data;
using Coworkee.Contracts.Identity;
using Coworkee.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Tests;

public sealed class GroupImportTests(IdentityApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync()
    {
        await app.ResetAllAsync();
        _setup = await app.SetupAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task Rows_create_groups_under_existing_parents()
    {
        await CreateAsync("Customer A");

        var result = await ImportAsync("Name,Description,ParentPath\r\nHamburg,Port,Customer A\r\nRoot2,,\r\n");

        result.Errors.ShouldBeEmpty();
        var groups = await GroupsAsync();
        groups["Hamburg"].Code.ShouldBe("00001.00001");
        groups["Hamburg"].Description.ShouldBe("Port");
        groups["Hamburg"].ParentId.ShouldBe(groups["Customer A"].Id);
        groups["Root2"].ParentId.ShouldBeNull();
    }

    [Fact]
    public async Task Rows_in_file_order_can_build_a_tree()
    {
        var result = await ImportAsync("Name,Description,ParentPath\r\nA,,\r\nB,,A\r\nC,,A/B\r\n");

        result.Errors.ShouldBeEmpty();
        var groups = await GroupsAsync();
        groups["B"].ParentId.ShouldBe(groups["A"].Id);
        groups["C"].Code.ShouldBe("00001.00001.00001");
    }

    [Fact]
    public async Task Unknown_parent_path_is_an_error_row()
    {
        var result = await ImportAsync("Name,Description,ParentPath\r\nX,,Nowhere\r\nY,,\r\n");

        result.Imported.ShouldBe(1);
        result.Errors.Single().Row.ShouldBe(2);
        result.Errors.Single().Message.ShouldBe("ParentPath: No group exists at this parent path.");
        (await GroupsAsync()).Keys.ShouldBe(["Y"]);
    }

    [Fact]
    public async Task Same_path_updates_the_description()
    {
        await ImportAsync("Name,Description,ParentPath\r\nA,,\r\nB,old,A\r\n");

        var result = await ImportAsync("Name,Description,ParentPath\r\nB,new , A \r\n");

        result.Errors.ShouldBeEmpty();
        var groups = await app.InDbAsync(_setup.TenantId, db => db.Set<UserGroup>().ToListAsync(Ct));
        groups.Count.ShouldBe(2);
        groups.Single(g => g.Name == "B").Description.ShouldBe("new");
    }

    [Fact]
    public async Task A_dry_run_writes_nothing_and_accepts_a_parent_from_an_earlier_row()
    {
        using var form = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes("Name,ParentPath\r\nA,\r\nB,A\r\n")), "file", "groups.csv" } };
        var sheet = (await (await Admin.PostAsync("/api/v1/data/Groups/import/file", form, Ct)).Content.ReadFromJsonAsync<ImportSheetDto>(Ct))!;

        var preview = (await (await Admin.PostAsJsonAsync($"/api/v1/data/imports/{sheet.JobId}/preview", new ImportSettings(["Name", "ParentPath"]), Ct)).Content.ReadFromJsonAsync<ImportPreviewDto>(Ct))!;

        preview.Valid.ShouldBe(2);
        (await GroupsAsync()).ShouldBeEmpty();
    }

    private async Task<ImportResult> ImportAsync(string csv)
    {
        using var form = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "groups.csv" } };
        var response = await Admin.PostAsync("/api/v1/data/Groups/import", form, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ImportResult>(Ct))!;
    }

    private async Task CreateAsync(string name) =>
        (await Admin.PostAsJsonAsync("/api/v1/identity/groups", new GroupRequest(name, null), Ct)).EnsureSuccessStatusCode();

    private async Task<Dictionary<string, UserGroup>> GroupsAsync() =>
        await app.InDbAsync(_setup.TenantId, db => db.Set<UserGroup>().ToDictionaryAsync(g => g.Name, Ct));
}
