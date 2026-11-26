using System.Linq.Expressions;
using System.Net.Http.Json;
using Coworkee.Contracts.DataFilters;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Query;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Coworkee.OData.DataFilters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.OData.Tests;

// layered data filters: which rows the resolver lets a user see. Crates come through their unit (IGroupRowSource), gadgets through no group.
public sealed class DataFilterResolverTests(ODataApp app) : IAsyncLifetime
{
    private static readonly Guid A = FakeGroupScope.A;
    private static readonly Guid B = FakeGroupScope.B;
    private static readonly Guid Parent = Guid.Parse("0197a000-0000-7000-8000-0000000000a1");
    private static readonly Guid Child = Guid.Parse("0197a000-0000-7000-8000-0000000000a2");
    private static readonly Guid Elsewhere = Guid.Parse("0197a000-0000-7000-8000-0000000000ff");

    private SetupResultDto _setup = null!;

    private FakeGroupScope Groups => app.App.Services.GetRequiredService<FakeGroupScope>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        await app.InDbAsync(_setup.TenantId, async db =>
        {
            db.AddRange(
                Gadget("Hammer", "Tools", 5), Gadget("Hook", "Tools", 1), Gadget("Hat", "Junk", 5), Gadget("Rag", "Junk", 1),
                Crate("a-small", 1, A), Crate("a-big", 10, A), Crate("b-small", 1, B), Crate("b-big", 10, B),
                Crate("p-small", 1, Parent), Crate("p-big", 10, Parent), Crate("c-small", 1, Child), Crate("c-big", 10, Child), Crate("x-big", 10, Child),
                Crate("z-big", 10, Elsewhere));
            return await db.SaveChangesAsync(Ct);
        });
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task No_filters_no_predicate()
    {
        var user = await UserAsync();
        // a filter of another set, and one of another organisation on this set
        await FilterAsync("Gadgets", DataFilterLayer.Tenant, null, Rule("Category", QueryOperator.Equal, "Tools"));
        await FilterAsync("Crates", DataFilterLayer.Tenant, null, Rule("Quantity", QueryOperator.GreaterThan, "5"), tenantId: Guid.NewGuid());

        (await PredicateAsync<Crate>(user, "Crates")).ShouldBeNull();
        app.Commands.Clear();
        (await PredicateAsync<Crate>(user, "Crates")).ShouldBeNull();
        // the filters of the organisation come from the cache
        app.Commands.Count("DataFilters").ShouldBe(0);
        (await CratesAsync(user)).Length.ShouldBe(10);
    }

    [Fact]
    public async Task System_tenant_and_role_filters_all_restrict()
    {
        var role = await RoleAsync("Buyer");
        var notMine = await RoleAsync("Seller");
        var user = await UserAsync(role);
        await FilterAsync("Gadgets", DataFilterLayer.System, null, Rule("Price", QueryOperator.GreaterThan, "1"));
        await FilterAsync("Gadgets", DataFilterLayer.Tenant, null, Rule("Category", QueryOperator.NotEqual, "Junk"));
        await FilterAsync("Gadgets", DataFilterLayer.Role, role, Rule("Name", QueryOperator.StartsWith, "H"));
        await FilterAsync("Gadgets", DataFilterLayer.Role, notMine, Rule("Name", QueryOperator.Equal, "Nothing"));
        // another organisation's filter naming the user's role does not apply here
        await FilterAsync("Gadgets", DataFilterLayer.Role, role, Rule("Name", QueryOperator.Equal, "Nothing"), tenantId: Guid.NewGuid());

        (await GadgetsAsync(user)).ShouldBe(["Hammer"]);
    }

    [Fact]
    public async Task Only_the_active_role_restricts_when_the_switch_is_on()
    {
        var tools = await RoleAsync("Tools");
        var dear = await RoleAsync("Dear");
        var user = await UserAsync(tools, dear);
        await FilterAsync("Gadgets", DataFilterLayer.Role, tools, Rule("Category", QueryOperator.Equal, "Tools"));
        await FilterAsync("Gadgets", DataFilterLayer.Role, dear, Rule("Price", QueryOperator.GreaterThan, "1"));

        // no role picked: every role restricts
        (await GadgetsAsync(user)).ShouldBe(["Hammer"]);
        (await app.As(user, _setup.TenantId).PutAsJsonAsync("/api/v1/identity/roles/me/active", new SetActiveRoleRequest(tools), Ct)).EnsureSuccessStatusCode();
        (await GadgetsAsync(user)).ShouldBe(["Hammer", "Hook"]);
    }

    [Fact]
    public async Task A_filtered_group_does_not_cut_an_unfiltered_groups_rows()
    {
        var user = await UserAsync();
        await FilterAsync("Crates", DataFilterLayer.Group, A, Rule("Quantity", QueryOperator.GreaterThan, "5"));

        // B's rows stay complete; rows of no group of the user do not come through
        (await CratesAsync(user)).ShouldBe(["a-big", "b-big", "b-small"]);
    }

    [Fact]
    public async Task A_child_group_gets_its_ancestors_filters()
    {
        Groups.Nodes = [new(Parent, null, "00001", "Parent"), new(Child, Parent, "00001.00001", "Child"), new(B, null, "00002", "B")];
        var user = await UserAsync();
        await FilterAsync("Crates", DataFilterLayer.Group, Parent, Rule("Quantity", QueryOperator.GreaterThan, "5"));
        await FilterAsync("Crates", DataFilterLayer.Group, Child, Rule("Name", QueryOperator.StartsWith, "c-"));

        // a member of the child only: the parent's filter applies too
        Groups.Accessible = [Child];
        (await CratesAsync(user)).ShouldBe(["c-big"]);
        // a member of the parent reaches the child: the parent's rows pass its filter, the child's rows both
        Groups.Accessible = [Parent, Child, B];
        (await CratesAsync(user)).ShouldBe(["b-big", "b-small", "c-big", "p-big"]);
    }

    [Fact]
    public async Task Without_a_row_source_any_groups_filter_lets_rows_through()
    {
        var user = await UserAsync();
        await FilterAsync("Gadgets", DataFilterLayer.Group, A, Rule("Category", QueryOperator.Equal, "Tools"));
        await FilterAsync("Gadgets", DataFilterLayer.Group, B, Rule("Price", QueryOperator.GreaterThan, "4"));

        (await GadgetsAsync(user)).ShouldBe(["Hammer", "Hat", "Hook"]);
        // a group without a filter lets everything through
        Groups.Accessible = [A, B, Elsewhere];
        (await PredicateAsync<Gadget>(user, "Gadgets")).ShouldBeNull();
    }

    [Fact]
    public async Task A_user_without_groups_gets_no_group_layer()
    {
        Groups.Accessible = [];
        var user = await UserAsync();
        await FilterAsync("Crates", DataFilterLayer.Group, A, Rule("Quantity", QueryOperator.GreaterThan, "5"));
        await FilterAsync("Crates", DataFilterLayer.Tenant, null, Rule("Name", QueryOperator.EndsWith, "big"));

        (await CratesAsync(user)).ShouldBe(["a-big", "b-big", "c-big", "p-big", "x-big", "z-big"]);
    }

    [Fact]
    public async Task ScopeAll_skips_the_group_layer()
    {
        var user = await UserAsync();
        await GrantAsync(user, IdentityPermissions.Groups.ScopeAll);
        await FilterAsync("Crates", DataFilterLayer.Group, A, Rule("Quantity", QueryOperator.LessThan, "5"));
        await FilterAsync("Crates", DataFilterLayer.Group, B, Rule("Quantity", QueryOperator.LessThan, "5"));
        await FilterAsync("Crates", DataFilterLayer.Tenant, null, Rule("Name", QueryOperator.EndsWith, "big"));

        // the tenant filter still applies
        (await CratesAsync(user)).ShouldBe(["a-big", "b-big", "c-big", "p-big", "x-big", "z-big"]);
    }

    [Fact]
    public async Task Bypass_skips_every_layer()
    {
        var role = await RoleAsync("Buyer");
        var user = await UserAsync(role);
        await GrantAsync(user, DataFilterPermissions.Bypass);
        await FilterAsync("Crates", DataFilterLayer.System, null, Rule("Quantity", QueryOperator.GreaterThan, "100"));
        await FilterAsync("Crates", DataFilterLayer.Tenant, null, Rule("Quantity", QueryOperator.GreaterThan, "100"));
        await FilterAsync("Crates", DataFilterLayer.Role, role, Rule("Quantity", QueryOperator.GreaterThan, "100"));
        await FilterAsync("Crates", DataFilterLayer.Group, A, Rule("Quantity", QueryOperator.GreaterThan, "100"));

        (await PredicateAsync<Crate>(user, "Crates")).ShouldBeNull();
    }

    [Fact]
    public async Task A_broken_filter_shows_nothing_and_does_not_throw()
    {
        var user = await UserAsync();
        // saving refuses both; a renamed property or a damaged row leaves them in the table
        var broken = await FilterAsync("Gadgets", DataFilterLayer.Tenant, null, Rule("NoSuchProperty", QueryOperator.Equal, "x"));
        await FilterAsync("Crates", DataFilterLayer.Group, A, "not a query");

        (await GadgetsAsync(user)).ShouldBeEmpty();
        (await GadgetsAsync(user)).ShouldBeEmpty();
        // only the broken group's rows go
        (await CratesAsync(user)).ShouldBe(["b-big", "b-small"]);
        app.Logs.Warnings.Count(w => w.Contains(broken.ToString(), StringComparison.Ordinal)).ShouldBe(1);
    }

    [Fact]
    public async Task Details_switch_off_skips_the_filter_for_key_reads()
    {
        var user = await UserAsync();
        await FilterAsync("Gadgets", DataFilterLayer.System, null, Rule("Price", QueryOperator.GreaterThan, "1"));
        await FilterAsync("Gadgets", DataFilterLayer.Tenant, null, Rule("Category", QueryOperator.Equal, "Tools"), appliesToDetails: false);

        (await GadgetsAsync(user)).ShouldBe(["Hammer"]);
        (await GadgetsAsync(user, byKey: true)).ShouldBe(["Hammer", "Hat"]);
    }

    private Gadget Gadget(string name, string category, decimal price) => new() { Name = name, Category = category, Price = price, TenantId = _setup.TenantId };

    private Crate Crate(string name, int quantity, Guid unit) => new() { Name = name, Quantity = quantity, UnitId = unit, TenantId = _setup.TenantId };

    private static string Rule(string field, QueryOperator op, string value) =>
        QueryJson.Serialize(new QueryGroup { Children = [new QueryRule { Field = field, Operator = op, Value = value }] });

    private async Task<Guid> FilterAsync(string set, DataFilterLayer layer, Guid? key, string query, bool appliesToDetails = true, Guid? tenantId = null)
    {
        await using var scope = app.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CoworkeeDbContext>();
        var filter = new DataFilter
        {
            TenantId = layer == DataFilterLayer.System ? null : tenantId ?? _setup.TenantId,
            EntitySet = set, Layer = layer, LayerKey = key, Query = query, AppliesToDetails = appliesToDetails, Version = 1,
        };
        db.Add(filter);
        await db.SaveChangesAsync(Ct);
        return filter.Id;
    }

    private async Task<Expression<Func<T, bool>>?> PredicateAsync<T>(Guid user, string set, bool byKey = false)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(user, _setup.TenantId));
        await using var scope = app.App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<DataFilterResolver>().PredicateAsync<T>(set, byKey, Ct);
    }

    private Task<string[]> GadgetsAsync(Guid user, bool byKey = false) => VisibleAsync<Gadget>(user, "Gadgets", g => g.Name, byKey);

    private Task<string[]> CratesAsync(Guid user) => VisibleAsync<Crate>(user, "Crates", c => c.Name, byKey: false);

    // the predicate inside a query of the database, as the OData reads will use it
    private async Task<string[]> VisibleAsync<T>(Guid user, string set, Expression<Func<T, string>> name, bool byKey)
        where T : class
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(user, _setup.TenantId));
        await using var scope = app.App.Services.CreateAsyncScope();
        var predicate = await scope.ServiceProvider.GetRequiredService<DataFilterResolver>().PredicateAsync<T>(set, byKey, Ct);
        var rows = scope.ServiceProvider.GetRequiredService<GadgetDbContext>().Set<T>().AsNoTracking();
        return [.. (await (predicate is null ? rows : rows.Where(predicate)).Select(name).ToListAsync(Ct)).Order(StringComparer.Ordinal)];
    }

    private HttpClient Admin() => app.As(_setup.AdminUserId, _setup.TenantId);

    private async Task<Guid> UserAsync(params Guid[] roles)
    {
        var response = await Admin().PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest($"u-{Guid.NewGuid():N}@acme.test", "Passw0rd!x", null, null), Ct);
        response.EnsureSuccessStatusCode();
        var user = (await response.Content.ReadFromJsonAsync<UserDto>(Ct))!.Id;
        if (roles.Length > 0)
        {
            (await Admin().PutAsJsonAsync($"/api/v1/identity/users/{user}/roles", new IdListRequest([.. roles]), Ct)).EnsureSuccessStatusCode();
        }

        return user;
    }

    private async Task<Guid> RoleAsync(string name)
    {
        var response = await Admin().PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest(name, null), Ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Guid>(Ct);
    }

    private async Task GrantAsync(Guid user, string permission)
    {
        await using var scope = app.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CoworkeeDbContext>();
        db.Add(new Coworkee.Identity.Domain.PermissionGrant { TenantId = null, Name = permission, ProviderType = PermissionProviderType.User, ProviderKey = user });
        await db.SaveChangesAsync(Ct);
    }
}
