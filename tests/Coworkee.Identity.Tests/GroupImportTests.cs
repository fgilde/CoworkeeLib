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
    public async Task A_blank_name_is_an_error_row()
    {
        var result = await ImportAsync("Name,Description,ParentPath\r\n,x,\r\nOk,,\r\n");

        result.Imported.ShouldBe(1);
        result.Errors.Single().Row.ShouldBe(2);
        result.Errors.Single().Message.ShouldContain("Name");
        (await GroupsAsync()).Keys.ShouldBe(["Ok"]);
    }

    [Fact]
    public async Task A_preview_without_a_name_column_reports_error_rows()
    {
        var preview = await PreviewAsync("ParentPath\r\nA\r\n", new ImportSettings(["ParentPath"]));

        preview.Valid.ShouldBe(0);
        preview.Invalid.Single().Errors.ShouldContain(e => e.Contains("Name"));
    }

    [Fact]
    public async Task Names_with_a_slash_are_error_rows()
    {
        var result = await ImportAsync("Name\r\nA/B\r\n");

        result.Errors.Single().Message.ShouldContain("/");
        (await GroupsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Duplicate_rows_in_one_file_make_one_group()
    {
        var result = await ImportAsync("Name,Description,ParentPath\r\nA,first,\r\nA,second,\r\n");

        result.Errors.ShouldBeEmpty();
        var groups = await app.InDbAsync(_setup.TenantId, db => db.Set<UserGroup>().ToListAsync(Ct));
        groups.ShouldHaveSingleItem().Description.ShouldBe("second");
    }

    [Fact]
    public async Task Users_without_Manage_get_error_rows_and_nothing_is_created()
    {
        var user = (await (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest("viewer@acme.test", "Passw0rd!x", "V", "W"), Ct)).Content.ReadFromJsonAsync<UserDto>(Ct))!;
        var role = await (await Admin.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest("GroupViewers", null), Ct)).Content.ReadFromJsonAsync<Guid>(Ct);
        (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{role}", new NameListRequest([IdentityPermissions.Groups.View]), Ct)).EnsureSuccessStatusCode();
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{user.Id}/roles", new IdListRequest([role]), Ct)).EnsureSuccessStatusCode();

        var result = await ImportAsync("Name\r\nA\r\nB\r\n", app.As(user.Id, _setup.TenantId));

        (result.Imported, result.Errors.Count).ShouldBe((0, 2));
        result.Errors[0].Message.ShouldContain(IdentityPermissions.Groups.Manage);
        (await GroupsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Another_tenant_with_the_same_names_stays_separate()
    {
        var other = await app.InDbAsync(null, db =>
        {
            var tenant = new Tenant { Name = "Other", Identifier = "other" };
            db.Add(tenant);
            db.Add(new UserGroup { Name = "A", Description = "theirs", TenantId = tenant.Id, Code = "00001" });
            return Task.FromResult(tenant.Id);
        });

        var result = await ImportAsync("Name,Description,ParentPath\r\nA,mine,\r\nB,,A\r\n");

        result.Errors.ShouldBeEmpty();
        (await app.InDbAsync(other, db => db.Set<UserGroup>().ToListAsync(Ct))).ShouldHaveSingleItem().Description.ShouldBe("theirs");
        var mine = await GroupsAsync();
        mine.Keys.ShouldBe(["A", "B"], ignoreOrder: true);
        mine["A"].Description.ShouldBe("mine");
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

    [Fact]
    public async Task The_export_has_the_parent_path_and_imports_into_another_tenant_as_the_same_tree()
    {
        await ImportAsync("Name,Description,ParentPath\r\nCustomer A,Big,\r\nHamburg,Port,Customer A\r\nAltona,,Customer A/Hamburg\r\nSupplier,,\r\n");

        var csv = await Admin.GetStringAsync("/api/v1/data/Groups/export?format=csv&columns=Name,Description,ParentPath&$orderby=Code", Ct);

        csv.Split('\n').Select(l => l.Trim('\r', '\uFEFF')).ShouldContain("Altona;;Customer A/Hamburg");
        (await Admin.GetStringAsync("/odata/Groups?$filter=ParentPath eq 'Customer A/Hamburg'&$select=Name", Ct)).ShouldContain("Altona");
        var (tenant, client) = await OtherTenantAdminAsync();
        (await ImportAsync(csv, client)).Errors.ShouldBeEmpty();
        var mine = await app.InDbAsync(_setup.TenantId, db => db.Set<UserGroup>().ToDictionaryAsync(g => g.Name, g => (g.Code, g.Description), Ct));
        (await app.InDbAsync(tenant, db => db.Set<UserGroup>().ToDictionaryAsync(g => g.Name, g => (g.Code, g.Description), Ct))).ShouldBe(mine, ignoreOrder: true);
    }

    private async Task<(Guid Tenant, HttpClient Client)> OtherTenantAdminAsync()
    {
        var (tenant, user) = await app.InDbAsync(null, db =>
        {
            var tenant = new Tenant { Name = "Other", Identifier = "other" };
            var user = new User { UserName = "admin@other.test", Email = "admin@other.test", TenantId = tenant.Id };
            db.AddRange(tenant, user);
            db.AddRange(new[] { IdentityPermissions.Groups.View, IdentityPermissions.Groups.Manage }.Select(name =>
                new PermissionGrant { Name = name, ProviderType = PermissionProviderType.User, ProviderKey = user.Id, TenantId = tenant.Id }));
            return Task.FromResult((tenant.Id, user.Id));
        });
        return (tenant, app.As(user, tenant));
    }

    private async Task<ImportPreviewDto> PreviewAsync(string csv, ImportSettings settings)
    {
        using var form = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "groups.csv" } };
        var sheet = (await (await Admin.PostAsync("/api/v1/data/Groups/import/file", form, Ct)).Content.ReadFromJsonAsync<ImportSheetDto>(Ct))!;
        var response = await Admin.PostAsJsonAsync($"/api/v1/data/imports/{sheet.JobId}/preview", settings, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ImportPreviewDto>(Ct))!;
    }

    private async Task<ImportResult> ImportAsync(string csv, HttpClient? client = null)
    {
        using var form = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "groups.csv" } };
        var response = await (client ?? Admin).PostAsync("/api/v1/data/Groups/import", form, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ImportResult>(Ct))!;
    }

    private async Task CreateAsync(string name) =>
        (await Admin.PostAsJsonAsync("/api/v1/identity/groups", new GroupRequest(name, null), Ct)).EnsureSuccessStatusCode();

    private async Task<Dictionary<string, UserGroup>> GroupsAsync() =>
        await app.InDbAsync(_setup.TenantId, db => db.Set<UserGroup>().ToDictionaryAsync(g => g.Name, Ct));
}
