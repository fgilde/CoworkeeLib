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

// which sets carry filters of the layer the administrator edits
public sealed class DataFilterOverviewTests(ODataApp app) : IAsyncLifetime
{
    private const string Root = "/api/v1/data-filters";

    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    private static string Query() => QueryJson.Serialize(new QueryGroup { Children = [new QueryRule { Field = "Name", Operator = QueryOperator.Equal, Value = "x" }] });

    private async Task SaveAsync(string set, DataFilterLayer layer) =>
        (await Admin.PutAsJsonAsync(Root, new SaveDataFilterRequest(set, layer, null, Query()), CoworkeeJson.Web, Ct)).EnsureSuccessStatusCode();

    [Fact]
    public async Task Overview_in_the_system_tenant_counts_system_filters_and_links()
    {
        await SaveAsync("Gadgets", DataFilterLayer.System);
        await SaveAsync("Widgets", DataFilterLayer.Tenant);
        (await Admin.PutAsJsonAsync(Root + "/links", new SaveDataFilterLinkRequest("Gadgets", "Id", "Widgets"), CoworkeeJson.Web, Ct)).EnsureSuccessStatusCode();

        var overview = (await Admin.GetFromJsonAsync<DataFilterOverviewDto>(Root + "/overview", CoworkeeJson.Web, Ct))!;

        overview.Layer.ShouldBe(DataFilterLayer.System);
        overview.Sets.Single(s => s.EntitySet == "Gadgets").ShouldBe(new DataFilterSetStateDto("Gadgets", 1, true));
        overview.Sets.Single(s => s.EntitySet == "Widgets").ShouldBe(new DataFilterSetStateDto("Widgets", 0, false));
    }

    [Fact]
    public async Task Overview_in_a_tenant_counts_its_tenant_filters_and_names_it()
    {
        await SaveAsync("Gadgets", DataFilterLayer.System);
        await SaveAsync("Widgets", DataFilterLayer.Tenant);
        (await Admin.PutAsJsonAsync(Root + "/links", new SaveDataFilterLinkRequest("Gadgets", "Id", "Widgets"), CoworkeeJson.Web, Ct)).EnsureSuccessStatusCode();
        await using (var scope = app.App.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<Coworkee.Infrastructure.Persistence.CoworkeeDbContext>().Set<Tenant>().IgnoreQueryFilters()
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.IsDefault, false), Ct);
        }

        await using var other = await app.StartAnotherAsync(new Dictionary<string, string>());
        var overview = (await other.GetTestClient().AsUser(_setup.AdminUserId, _setup.TenantId).GetFromJsonAsync<DataFilterOverviewDto>(Root + "/overview", CoworkeeJson.Web, Ct))!;

        overview.Layer.ShouldBe(DataFilterLayer.Tenant);
        overview.TenantName.ShouldBe("Acme");
        overview.Sets.Single(s => s.EntitySet == "Widgets").ShouldBe(new DataFilterSetStateDto("Widgets", 1, false));
        overview.Sets.Single(s => s.EntitySet == "Gadgets").ShouldBe(new DataFilterSetStateDto("Gadgets", 0, false));
    }

    [Fact]
    public async Task Overview_needs_manage_system_or_manage_tenant()
    {
        var email = $"ov-{Guid.NewGuid():N}@acme.test";
        var user = await (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null), Ct)).Content.ReadFromJsonAsync<UserDto>(Ct);
        await app.InDbAsync(_setup.TenantId, async db =>
        {
            db.Add(new PermissionGrant { TenantId = null, Name = DataFilterPermissions.ManageGroups, ProviderType = PermissionProviderType.User, ProviderKey = user!.Id });
            return await db.SaveChangesAsync(Ct);
        });

        (await app.As(user!.Id, _setup.TenantId).GetAsync(Root + "/overview", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
