using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts;
using Coworkee.Contracts.DataFilters;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Query;
using Coworkee.Infrastructure.Persistence;
using Coworkee.OData.DataFilters;
using Coworkee.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.OData.Tests;

// layered data filters: who may write which layer, what is refused, where a filter is stored
public sealed class DataFilterCommandTests(ODataApp app) : IAsyncLifetime
{
    private const string Root = "/api/v1/data-filters";

    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Query(string field = "Name") =>
        QueryJson.Serialize(new QueryGroup { Children = [new QueryRule { Field = field, Operator = QueryOperator.Equal, Value = "x" }] });

    private static SaveDataFilterRequest Request(DataFilterLayer layer, string field = "Name") =>
        new("Gadgets", layer, layer is DataFilterLayer.Group or DataFilterLayer.Role ? Guid.NewGuid() : null, Query(field));

    private HttpClient Admin() => app.As(_setup.AdminUserId, _setup.TenantId);

    private async Task<HttpClient> PlainUserAsync()
    {
        var email = $"plain-{Guid.NewGuid():N}@acme.test";
        var user = await (await Admin().PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null), Ct)).Content.ReadFromJsonAsync<UserDto>(Ct);
        return app.As(user!.Id, _setup.TenantId);
    }

    private async Task<T> InDbAsync<T>(Func<CoworkeeDbContext, Task<T>> read)
    {
        await using var scope = app.App.Services.CreateAsyncScope();
        return await read(scope.ServiceProvider.GetRequiredService<CoworkeeDbContext>());
    }

    [Theory]
    [InlineData(DataFilterLayer.System)]
    [InlineData(DataFilterLayer.Tenant)]
    [InlineData(DataFilterLayer.Group)]
    [InlineData(DataFilterLayer.Role)]
    public async Task Saving_needs_the_permission_of_its_layer(DataFilterLayer layer)
    {
        var plain = await PlainUserAsync();

        (await plain.PutAsJsonAsync(Root, Request(layer), CoworkeeJson.Web, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Admin().PutAsJsonAsync(Root, Request(layer), CoworkeeJson.Web, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await plain.GetAsync(Root, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await plain.PutAsJsonAsync(Root + "/links", new SaveDataFilterLinkRequest("Gadgets", "Id", "Widgets"), CoworkeeJson.Web, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_filter_that_does_not_parse_is_refused()
    {
        var response = await Admin().PutAsJsonAsync(Root, Request(DataFilterLayer.Tenant, field: "NoSuchProperty"), CoworkeeJson.Web, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("NoSuchProperty");
        (await Admin().GetFromJsonAsync<DataFilterDto[]>(Root, CoworkeeJson.Web, Ct))!.ShouldBeEmpty();
    }

    [Fact]
    public async Task Saving_twice_replaces_the_filter_and_the_list_reports_it_valid()
    {
        var first = await (await Admin().PutAsJsonAsync(Root, Request(DataFilterLayer.Tenant), CoworkeeJson.Web, Ct)).Content.ReadFromJsonAsync<DataFilterDto>(CoworkeeJson.Web, Ct);
        var second = await (await Admin().PutAsJsonAsync(Root, Request(DataFilterLayer.Tenant, "Category") with { AppliesToDetails = false }, CoworkeeJson.Web, Ct)).Content.ReadFromJsonAsync<DataFilterDto>(CoworkeeJson.Web, Ct);

        second!.Id.ShouldBe(first!.Id);
        var listed = (await Admin().GetFromJsonAsync<DataFilterDto[]>($"{Root}?entitySet=Gadgets&layer=Tenant", CoworkeeJson.Web, Ct))!.ShouldHaveSingleItem();
        (listed.Valid, listed.AppliesToDetails, listed.Query.Contains("Category", StringComparison.Ordinal)).ShouldBe((true, false, true));
        (await InDbAsync(db => db.Set<DataFilter>().Select(f => f.Version).SingleAsync(Ct))).ShouldBe(2);
        (await Admin().DeleteAsync($"{Root}/{first.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Admin().GetFromJsonAsync<DataFilterDto[]>(Root, CoworkeeJson.Web, Ct))!.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_link_across_contexts_or_to_a_missing_set_is_refused()
    {
        // Gadgets live in GadgetDbContext, Parcels in ParcelDbContext
        (await Admin().PutAsJsonAsync(Root + "/links", new SaveDataFilterLinkRequest("Gadgets", "Id", "Parcels"), CoworkeeJson.Web, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin().PutAsJsonAsync(Root + "/links", new SaveDataFilterLinkRequest("Gadgets", "Id", "NoSuchSet"), CoworkeeJson.Web, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin().PutAsJsonAsync(Root + "/links", new SaveDataFilterLinkRequest("Gadgets", "NoSuchProperty", "Widgets"), CoworkeeJson.Web, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin().GetFromJsonAsync<DataFilterLinkDto[]>(Root + "/links", CoworkeeJson.Web, Ct))!.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_link_inside_one_context_is_saved_once_per_set()
    {
        // both sets are in GadgetDbContext
        (await Admin().PutAsJsonAsync(Root + "/links", new SaveDataFilterLinkRequest("Gadgets", "Id", "Widgets"), CoworkeeJson.Web, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Admin().PutAsJsonAsync(Root + "/links", new SaveDataFilterLinkRequest("Gadgets", "Id", "Lockets"), CoworkeeJson.Web, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var link = (await Admin().GetFromJsonAsync<DataFilterLinkDto[]>(Root + "/links", CoworkeeJson.Web, Ct))!.ShouldHaveSingleItem();
        link.TargetSet.ShouldBe("Lockets");
        (await Admin().DeleteAsync($"{Root}/links/{link.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Tenant_group_and_role_filters_stay_in_their_tenant()
    {
        var other = Guid.NewGuid();
        await using (var scope = app.App.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CoworkeeDbContext>();
            db.Add(new DataFilter { TenantId = other, EntitySet = "Gadgets", Layer = DataFilterLayer.Tenant, Query = Query() });
            await db.SaveChangesAsync(Ct);
        }

        foreach (var layer in new[] { DataFilterLayer.Tenant, DataFilterLayer.Group, DataFilterLayer.Role })
        {
            (await Admin().PutAsJsonAsync(Root, Request(layer), CoworkeeJson.Web, Ct)).EnsureSuccessStatusCode();
        }

        (await Admin().GetFromJsonAsync<DataFilterDto[]>(Root, CoworkeeJson.Web, Ct))!.Length.ShouldBe(3);
        var tenants = await InDbAsync(db => db.Set<DataFilter>().Where(f => f.Layer != DataFilterLayer.System).Select(f => f.TenantId).ToListAsync(Ct));
        tenants.Count(t => t == _setup.TenantId).ShouldBe(3);
        tenants.Count(t => t == other).ShouldBe(1);
        // a filter of another tenant cannot be deleted from here
        var foreign = await InDbAsync(db => db.Set<DataFilter>().Where(f => f.TenantId == other).Select(f => f.Id).SingleAsync(Ct));
        (await Admin().DeleteAsync($"{Root}/{foreign}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task System_filters_belong_to_no_tenant()
    {
        (await Admin().PutAsJsonAsync(Root, Request(DataFilterLayer.System), CoworkeeJson.Web, Ct)).EnsureSuccessStatusCode();

        (await InDbAsync(db => db.Set<DataFilter>().Where(f => f.Layer == DataFilterLayer.System).Select(f => f.TenantId).SingleAsync(Ct))).ShouldBeNull();
        (await Admin().GetFromJsonAsync<DataFilterDto[]>($"{Root}?layer=System", CoworkeeJson.Web, Ct))!.ShouldHaveSingleItem();
    }

    private async Task<(HttpClient Client, Guid UserId)> UserWithAsync(Guid tenantId, params string[] permissions)
    {
        var email = $"perm-{Guid.NewGuid():N}@acme.test";
        var user = await (await Admin().PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null), Ct)).Content.ReadFromJsonAsync<UserDto>(Ct);
        await using (var scope = app.App.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CoworkeeDbContext>();
            foreach (var name in permissions)
            {
                db.Add(new Coworkee.Identity.Domain.PermissionGrant { TenantId = null, Name = name, ProviderType = PermissionProviderType.User, ProviderKey = user!.Id });
            }

            await db.SaveChangesAsync(Ct);
        }

        return (app.As(user!.Id, tenantId), user.Id);
    }

    [Fact]
    public async Task A_user_without_an_organisation_cannot_save_a_filter_that_would_belong_to_nobody()
    {
        var (_, userId) = await UserWithAsync(_setup.TenantId, DataFilterPermissions.ManageTenant);

        using var actor = Coworkee.Core.Security.CurrentUserScope.Begin(new Coworkee.Core.Security.ImpersonatedUser(userId, null));
        await using var scope = app.App.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<Coworkee.Application.Messaging.IDispatcher>()
            .SendAsync(new SaveDataFilterCommand(Request(DataFilterLayer.Tenant)), Ct);

        result.IsSuccess.ShouldBeFalse();
        (await InDbAsync(db => db.Set<DataFilter>().IgnoreQueryFilters().CountAsync(Ct))).ShouldBe(0);
    }

    [Fact]
    public async Task An_admin_outside_the_system_tenant_cannot_write_system_filters_or_links()
    {
        var systemFilter = await (await Admin().PutAsJsonAsync(Root, Request(DataFilterLayer.System), CoworkeeJson.Web, Ct)).Content.ReadFromJsonAsync<DataFilterDto>(CoworkeeJson.Web, Ct);
        (await Admin().PutAsJsonAsync(Root + "/links", new SaveDataFilterLinkRequest("Gadgets", "Id", "Widgets"), CoworkeeJson.Web, Ct)).EnsureSuccessStatusCode();
        var link = (await Admin().GetFromJsonAsync<DataFilterLinkDto[]>(Root + "/links", CoworkeeJson.Web, Ct))!.Single();

        // the admin's organisation stops being the system (default) one: it still holds every permission, a plain tenant admin
        await using (var scope = app.App.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<CoworkeeDbContext>().Set<Coworkee.Identity.Domain.Tenant>().IgnoreQueryFilters()
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.IsDefault, false), Ct);
        }

        await using var other = await app.StartAnotherAsync(new Dictionary<string, string>());
        var client = other.GetTestClient().AsUser(_setup.AdminUserId, _setup.TenantId);
        (await client.PutAsJsonAsync(Root, Request(DataFilterLayer.System), CoworkeeJson.Web, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.DeleteAsync($"{Root}/{systemFilter!.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.PutAsJsonAsync(Root + "/links", new SaveDataFilterLinkRequest("Gadgets", "Id", "Lockets"), CoworkeeJson.Web, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.DeleteAsync($"{Root}/links/{link.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.PutAsJsonAsync(Root, Request(DataFilterLayer.Tenant), CoworkeeJson.Web, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_user_with_one_manage_permission_writes_only_that_layer()
    {
        var (client, _) = await UserWithAsync(_setup.TenantId, DataFilterPermissions.ManageGroups);
        var tenantFilter = await (await Admin().PutAsJsonAsync(Root, Request(DataFilterLayer.Tenant), CoworkeeJson.Web, Ct)).Content.ReadFromJsonAsync<DataFilterDto>(CoworkeeJson.Web, Ct);

        foreach (var layer in new[] { DataFilterLayer.System, DataFilterLayer.Tenant, DataFilterLayer.Role })
        {
            (await client.PutAsJsonAsync(Root, Request(layer), CoworkeeJson.Web, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        (await client.PutAsJsonAsync(Root + "/links", new SaveDataFilterLinkRequest("Gadgets", "Id", "Widgets"), CoworkeeJson.Web, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.DeleteAsync($"{Root}/{tenantFilter!.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var group = await (await client.PutAsJsonAsync(Root, Request(DataFilterLayer.Group), CoworkeeJson.Web, Ct)).Content.ReadFromJsonAsync<DataFilterDto>(CoworkeeJson.Web, Ct);
        // the list shows only what the user may manage
        (await client.GetFromJsonAsync<DataFilterDto[]>(Root, CoworkeeJson.Web, Ct))!.ShouldHaveSingleItem().Id.ShouldBe(group!.Id);
        (await client.DeleteAsync($"{Root}/{group.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task The_fields_of_a_set_flag_the_properties_only_some_users_see()
    {
        var fields = (await Admin().GetFromJsonAsync<DataFilterFieldDto[]>($"{Root}/fields/Gadgets", CoworkeeJson.Web, Ct))!;

        fields.Single(f => f.Name == "Name").Protected.ShouldBeFalse();
        fields.Single(f => f.Name == "Category").Protected.ShouldBeTrue();
        // hidden properties are not part of the model, so they are not offered
        fields.ShouldNotContain(f => f.Name == "SerialCode");
        (await Admin().GetFromJsonAsync<string[]>(Root + "/sets", CoworkeeJson.Web, Ct))!.ShouldContain("Gadgets");
        (await Admin().GetAsync($"{Root}/fields/NoSuchSet", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_filter_may_only_use_properties_the_editing_admin_may_read()
    {
        // Category is HideUnless(Gadgets.Categories)
        var (without, _) = await UserWithAsync(_setup.TenantId, DataFilterPermissions.ManageTenant);
        var (with, _) = await UserWithAsync(_setup.TenantId, DataFilterPermissions.ManageTenant, ODataApp.SeeCategories);

        (await without.PutAsJsonAsync(Root, Request(DataFilterLayer.Tenant, "Category"), CoworkeeJson.Web, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await without.PutAsJsonAsync(Root, Request(DataFilterLayer.Tenant), CoworkeeJson.Web, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await with.PutAsJsonAsync(Root, Request(DataFilterLayer.Tenant, "Category"), CoworkeeJson.Web, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_malformed_filter_is_refused_not_a_server_error()
    {
        // complete for the builder (one value), but the value is null: writing the $filter throws
        var query = QueryJson.Serialize(new QueryGroup { Children = [new QueryRule { Field = "Name", Operator = QueryOperator.In, Values = [null!] }] });

        (await Admin().PutAsJsonAsync(Root, new SaveDataFilterRequest("Gadgets", DataFilterLayer.Tenant, null, query), CoworkeeJson.Web, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin().PutAsJsonAsync(Root, new SaveDataFilterRequest("Gadgets", DataFilterLayer.Tenant, null, """{"children":[null]}"""), CoworkeeJson.Web, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_link_chain_that_comes_back_to_its_set_is_refused()
    {
        (await Admin().PutAsJsonAsync(Root + "/links", new SaveDataFilterLinkRequest("Gadgets", "Id", "Widgets"), CoworkeeJson.Web, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Admin().PutAsJsonAsync(Root + "/links", new SaveDataFilterLinkRequest("Widgets", "Id", "Lockets"), CoworkeeJson.Web, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await Admin().PutAsJsonAsync(Root + "/links", new SaveDataFilterLinkRequest("Lockets", "Id", "Gadgets"), CoworkeeJson.Web, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin().GetFromJsonAsync<DataFilterLinkDto[]>(Root + "/links", CoworkeeJson.Web, Ct))!.Length.ShouldBe(2);
        // moving the start of the chain to its end replaces that link: no loop
        (await Admin().PutAsJsonAsync(Root + "/links", new SaveDataFilterLinkRequest("Gadgets", "Id", "Lockets"), CoworkeeJson.Web, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_link_property_of_another_type_than_the_target_key_is_refused()
    {
        // Price is a decimal, the key of Widgets a Guid
        (await Admin().PutAsJsonAsync(Root + "/links", new SaveDataFilterLinkRequest("Gadgets", "Price", "Widgets"), CoworkeeJson.Web, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin().GetFromJsonAsync<DataFilterLinkDto[]>(Root + "/links", CoworkeeJson.Web, Ct))!.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_fields_and_sets_need_a_manage_permission()
    {
        var plain = await PlainUserAsync();

        (await plain.GetAsync($"{Root}/fields/Gadgets", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await plain.GetAsync(Root + "/sets", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
