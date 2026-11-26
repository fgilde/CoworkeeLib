using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts;
using Coworkee.Contracts.DataFilters;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Query;
using Coworkee.Infrastructure.Persistence;
using Coworkee.OData.DataFilters;
using Coworkee.Testing;
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
}
