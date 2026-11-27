using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts;
using Coworkee.Contracts.DataFilters;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Query;
using Coworkee.Identity.Domain;
using Coworkee.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.OData.Tests;

// the preview of a draft filter: how many of the rows the editor sees pass it
public sealed class DataFilterPreviewTests(ODataApp app) : IAsyncLifetime
{
    private const string Root = "/api/v1/data-filters/preview";

    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        var tenantId = _setup.TenantId;
        await app.InDbAsync(tenantId, async db =>
        {
            for (var i = 0; i < 10; i++)
            {
                db.Add(new Gadget { Name = i < 4 ? "Open" : "Closed", Category = "Tools", Price = i < 3 ? 1 : 50, TenantId = tenantId });
            }

            return await db.SaveChangesAsync(Ct);
        });
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Rule(string field, QueryOperator op, string value) =>
        QueryJson.Serialize(new QueryGroup { Children = [new QueryRule { Field = field, Operator = op, Value = value }] });

    private static PreviewDataFilterRequest Request(DataFilterLayer layer, string query) => new("Gadgets", layer, null, query);

    private async Task<HttpClient> UserWithAsync(params string[] permissions)
    {
        var admin = app.As(_setup.AdminUserId, _setup.TenantId);
        var email = $"prev-{Guid.NewGuid():N}@acme.test";
        var user = await (await admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null), Ct)).Content.ReadFromJsonAsync<UserDto>(Ct);
        await app.InDbAsync(_setup.TenantId, async db =>
        {
            foreach (var name in permissions)
            {
                db.Add(new PermissionGrant { TenantId = null, Name = name, ProviderType = PermissionProviderType.User, ProviderKey = user!.Id });
            }

            return await db.SaveChangesAsync(Ct);
        });
        return app.As(user!.Id, _setup.TenantId);
    }

    [Fact]
    public async Task Preview_counts_the_rows_the_editor_sees_and_those_passing_the_draft()
    {
        var admin = app.As(_setup.AdminUserId, _setup.TenantId);
        // a stored filter that would hide three rows: the preview ignores it
        (await admin.PutAsJsonAsync("/api/v1/data-filters", new SaveDataFilterRequest("Gadgets", DataFilterLayer.Tenant, null, Rule("Price", QueryOperator.GreaterThan, "5")), CoworkeeJson.Web, Ct)).EnsureSuccessStatusCode();

        var response = await admin.PostAsJsonAsync(Root, Request(DataFilterLayer.Tenant, Rule("Name", QueryOperator.Equal, "Open")), CoworkeeJson.Web, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<DataFilterPreviewDto>(CoworkeeJson.Web, Ct)).ShouldBe(new DataFilterPreviewDto(4, 10, false));
    }

    private Task SaveAsync(HttpClient client, DataFilterLayer layer, string query) =>
        client.PutAsJsonAsync("/api/v1/data-filters", new SaveDataFilterRequest("Gadgets", layer, null, query), CoworkeeJson.Web, Ct).ContinueWith(t => t.Result.EnsureSuccessStatusCode(), Ct);

    private static async Task<DataFilterPreviewDto> PreviewAsync(HttpClient client, DataFilterLayer layer, string query)
    {
        var response = await client.PostAsJsonAsync(Root, Request(layer, query), CoworkeeJson.Web, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<DataFilterPreviewDto>(CoworkeeJson.Web, Ct))!;
    }

    [Fact]
    public async Task Preview_counts_within_the_other_filters_that_bind_the_editor_but_not_the_stored_version_of_the_edited_filter()
    {
        var admin = app.As(_setup.AdminUserId, _setup.TenantId);
        var editor = await UserWithAsync(DataFilterPermissions.ManageTenant, ODataApp.ViewGadgets);
        // the System filter hides the three cheap rows from the editor; the stored Tenant filter is the one being edited
        await SaveAsync(admin, DataFilterLayer.System, Rule("Price", QueryOperator.GreaterThan, "1"));
        await SaveAsync(editor, DataFilterLayer.Tenant, Rule("Name", QueryOperator.Equal, "Closed"));

        // seven rows the editor sees, one of them open
        (await PreviewAsync(editor, DataFilterLayer.Tenant, Rule("Name", QueryOperator.Equal, "Open"))).ShouldBe(new DataFilterPreviewDto(1, 7, false));
    }

    [Fact]
    public async Task Preview_of_a_tenant_admin_outside_the_system_tenant_counts_within_the_System_filters()
    {
        var admin = app.As(_setup.AdminUserId, _setup.TenantId);
        await SaveAsync(admin, DataFilterLayer.System, Rule("Price", QueryOperator.GreaterThan, "1"));
        await using (var scope = app.App.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<Coworkee.Infrastructure.Persistence.CoworkeeDbContext>().Set<Tenant>().IgnoreQueryFilters()
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.IsDefault, false), Ct);
        }

        await using var other = await app.StartAnotherAsync(new Dictionary<string, string>());
        var tenantAdmin = other.GetTestClient().AsUser(_setup.AdminUserId, _setup.TenantId);

        // Bypass skips the tenant's own layers, not System: the count oracle stays closed
        (await PreviewAsync(tenantAdmin, DataFilterLayer.Tenant, Rule("Name", QueryOperator.Equal, "Open"))).ShouldBe(new DataFilterPreviewDto(1, 7, false));
    }

    [Fact]
    public async Task Preview_predicts_what_the_saved_filter_does()
    {
        var admin = app.As(_setup.AdminUserId, _setup.TenantId);
        var user = await UserWithAsync(ODataApp.ViewGadgets);
        var query = Rule("Name", QueryOperator.Contains, "pen");
        await app.InDbAsync(_setup.TenantId, async db =>
        {
            db.Add(new Gadget { Name = "Pencil", Category = "Tools", TenantId = _setup.TenantId });
            return await db.SaveChangesAsync(Ct);
        });

        var preview = (await (await admin.PostAsJsonAsync(Root, Request(DataFilterLayer.Tenant, query), CoworkeeJson.Web, Ct)).Content.ReadFromJsonAsync<DataFilterPreviewDto>(CoworkeeJson.Web, Ct))!;
        (await admin.PutAsJsonAsync("/api/v1/data-filters", new SaveDataFilterRequest("Gadgets", DataFilterLayer.Tenant, null, query), CoworkeeJson.Web, Ct)).EnsureSuccessStatusCode();

        // both paths share DataFilterQueries.ApplySettings; under EF a count cannot show OData's null guards, so this guards equality of the outcome
        int.Parse(await user.GetStringAsync("/odata/Gadgets/$count", Ct), System.Globalization.CultureInfo.InvariantCulture).ShouldBe((int)preview.Passing!.Value);
    }

    [Fact]
    public async Task Preview_needs_the_permission_of_its_layer()
    {
        var tenantManager = await UserWithAsync(DataFilterPermissions.ManageTenant, ODataApp.ViewGadgets);
        var nobody = await UserWithAsync(ODataApp.ViewGadgets);
        var query = Rule("Name", QueryOperator.Equal, "Open");

        (await nobody.PostAsJsonAsync(Root, Request(DataFilterLayer.Tenant, query), CoworkeeJson.Web, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await tenantManager.PostAsJsonAsync(Root, Request(DataFilterLayer.Tenant, query), CoworkeeJson.Web, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await tenantManager.PostAsJsonAsync(Root, Request(DataFilterLayer.Group, query) with { LayerKey = Guid.NewGuid() }, CoworkeeJson.Web, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // System needs ManageSystem in the system tenant: a tenant admin (holds every permission) is refused
        await using (var scope = app.App.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<Coworkee.Infrastructure.Persistence.CoworkeeDbContext>().Set<Tenant>().IgnoreQueryFilters()
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.IsDefault, false), Ct);
        }

        await using var other = await app.StartAnotherAsync(new Dictionary<string, string>());
        var admin = other.GetTestClient().AsUser(_setup.AdminUserId, _setup.TenantId);
        (await admin.PostAsJsonAsync(Root, Request(DataFilterLayer.System, query), CoworkeeJson.Web, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await admin.PostAsJsonAsync(Root, Request(DataFilterLayer.Tenant, query), CoworkeeJson.Web, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Preview_refuses_unknown_and_protected_properties()
    {
        // Category is HideUnless(SeeCategories)
        var without = await UserWithAsync(DataFilterPermissions.ManageTenant, ODataApp.ViewGadgets);

        var unknown = await without.PostAsJsonAsync(Root, Request(DataFilterLayer.Tenant, Rule("NoSuchProperty", QueryOperator.Equal, "x")), CoworkeeJson.Web, Ct);
        var protectedProperty = await without.PostAsJsonAsync(Root, Request(DataFilterLayer.Tenant, Rule("Category", QueryOperator.Equal, "Tools")), CoworkeeJson.Web, Ct);

        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await unknown.Content.ReadAsStringAsync(Ct)).ShouldContain("NoSuchProperty");
        protectedProperty.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await protectedProperty.Content.ReadAsStringAsync(Ct)).ShouldContain("you may not read");
    }

    [Fact]
    public async Task Preview_reports_a_timeout_instead_of_failing()
    {
        await using var other = await app.StartAnotherAsync(new Dictionary<string, string> { ["Coworkee:OData:DataFilterPreviewTimeout"] = TimeSpan.FromTicks(1).ToString() });
        var admin = other.GetTestClient().AsUser(_setup.AdminUserId, _setup.TenantId);

        var response = await admin.PostAsJsonAsync(Root, Request(DataFilterLayer.Tenant, Rule("Name", QueryOperator.Equal, "Open")), CoworkeeJson.Web, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<DataFilterPreviewDto>(CoworkeeJson.Web, Ct)).ShouldBe(new DataFilterPreviewDto(null, null, true));
    }
}
