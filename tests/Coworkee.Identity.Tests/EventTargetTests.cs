using System.Net.Http.Json;
using Coworkee.Application.Authorization;
using Coworkee.Application.Events;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Identity.Tests;

public sealed class EventTargetTests(IdentityApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;
    private readonly Dictionary<string, Guid> _users = [];
    private Guid _granted;
    private Guid _child;

    public async ValueTask InitializeAsync()
    {
        await app.ResetAllAsync();
        _setup = await app.SetupAsync();
        _users["admin"] = _setup.AdminUserId;

        // the matrix: every way to a permission, the ways that do not lead to it, and users who must never count
        var viewers = await RoleAsync("Viewers", IdentityPermissions.Users.View);
        await PutAsync($"/api/v1/identity/users/{await UserAsync("alice")}/roles", new IdListRequest([viewers]));
        await PutAsync($"/api/v1/identity/permissions/grants/User/{await UserAsync("bob")}", new NameListRequest([IdentityPermissions.Users.View]));
        _granted = await GroupAsync("Granted");
        await PutAsync($"/api/v1/identity/permissions/grants/Group/{_granted}", new NameListRequest([IdentityPermissions.Users.View, IdentityPermissions.Roles.View]));
        await PutAsync($"/api/v1/identity/groups/{_granted}/members", new IdListRequest([await UserAsync("carol")]));
        var withRole = await GroupAsync("With role");
        await PutAsync($"/api/v1/identity/groups/{withRole}/roles", new IdListRequest([viewers]));
        await PutAsync($"/api/v1/identity/groups/{withRole}/members", new IdListRequest([await UserAsync("dave")]));
        _child = await GroupAsync("Child", _granted);
        await PutAsync($"/api/v1/identity/groups/{_child}/members", new IdListRequest([await UserAsync("erin")]));
        await PutAsync($"/api/v1/identity/permissions/grants/User/{await UserAsync("frank")}", new NameListRequest([IdentityPermissions.Users.Impersonate]));
        await UserAsync("gina");
        var ivan = await UserAsync("ivan");
        await PutAsync($"/api/v1/identity/permissions/grants/User/{ivan}", new NameListRequest([IdentityPermissions.Users.View]));
        await PutAsync($"/api/v1/identity/users/{ivan}/roles", new IdListRequest([viewers]));
        await PutAsync($"/api/v1/identity/users/{ivan}", new UpdateUserRequest(null, null, false));
        _users["eve"] = await app.InDbAsync(null, async db =>
        {
            var tenant = new Tenant { Name = "Other", Identifier = "other" };
            var user = new User { TenantId = tenant.Id, UserName = "eve@other.test", Email = "eve@other.test", NormalizedUserName = "EVE@OTHER.TEST", NormalizedEmail = "EVE@OTHER.TEST" };
            var group = new UserGroup { TenantId = tenant.Id, Name = "Viewers" };
            var adminRole = await db.Set<Role>().Where(r => r.IsSystem && r.Name == SystemRoles.Admin).Select(r => r.Id).SingleAsync(Ct);
            db.AddRange(tenant, user, group, new IdentityUserRole<Guid> { UserId = user.Id, RoleId = adminRole }, new UserGroupMember { UserId = user.Id, GroupId = group.Id });
            db.Add(new PermissionGrant { Name = IdentityPermissions.Users.View, ProviderType = PermissionProviderType.User, ProviderKey = user.Id, TenantId = tenant.Id });
            return user.Id;
        });
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    [Theory]
    [InlineData(IdentityPermissions.Users.View)]
    [InlineData(IdentityPermissions.Users.Impersonate)]
    [InlineData(IdentityPermissions.Roles.View)]
    [InlineData("Nope.Undefined")]
    public async Task A_permission_target_names_exactly_the_users_the_permission_checker_grants(string permission)
    {
        var expected = new List<string>();
        foreach (var (name, id) in _users)
        {
            if (await CheckerGrantsAsync(id, permission))
            {
                expected.Add(name);
            }
        }

        Names(await UsersAsync(EventTarget.Permission(permission))).ShouldBe(expected, ignoreOrder: true);
    }

    [Fact]
    public async Task The_matrix_holds_the_expected_ways_to_a_permission()
    {
        Names(await UsersAsync(EventTarget.Permission(IdentityPermissions.Users.View))).ShouldBe(["admin", "alice", "bob", "carol", "dave", "frank"], ignoreOrder: true);
    }

    [Fact]
    public async Task Roles_and_groups_with_their_sub_groups_are_targets_too()
    {
        Names(await UsersAsync(EventTarget.Role("Viewers"))).ShouldBe(["alice", "dave"], ignoreOrder: true);
        Names(await UsersAsync(EventTarget.Role(SystemRoles.Admin))).ShouldBe(["admin"]);
        Names(await UsersAsync(EventTarget.Group(_granted))).ShouldBe(["carol", "erin"], ignoreOrder: true);
        Names(await UsersAsync(EventTarget.Group(_child))).ShouldBe(["erin"]);
        Names(await UsersAsync(EventTarget.All)).ShouldBe(["admin", "alice", "bob", "carol", "dave", "erin", "frank", "gina"], ignoreOrder: true);
    }

    [Fact]
    public async Task Ids_unions_and_exceptions_combine_and_current_is_the_acting_user()
    {
        var alice = _users["alice"];
        (await UsersAsync(EventTarget.Users(alice, _users["gina"]) | EventTarget.Group(_child))).ShouldBe([alice, _users["gina"], _users["erin"]], ignoreOrder: true);
        Names(await UsersAsync(EventTarget.Permission(IdentityPermissions.Users.View).Except(EventTarget.Current), actor: alice))
            .ShouldBe(["admin", "bob", "carol", "dave", "frank"], ignoreOrder: true);
        Names(await UsersAsync(EventTarget.All.Except(EventTarget.Role("Viewers") | EventTarget.Users(_users["gina"])))).ShouldBe(["admin", "bob", "carol", "erin", "frank"], ignoreOrder: true);
        (await UsersAsync(EventTarget.Current, actor: alice)).ShouldBe([alice]);
    }

    [Fact]
    public async Task User_ids_need_no_database_and_are_taken_as_given()
    {
        var unknown = Guid.CreateVersion7();
        var sql = SqlRecorder.Start();
        try
        {
            (await UsersAsync(EventTarget.Users(_users["alice"], unknown).Except(EventTarget.Current))).ShouldBe([_users["alice"], unknown], ignoreOrder: true);
        }
        finally
        {
            SqlRecorder.Stop();
        }

        sql.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_whole_predicate_runs_in_the_database_as_one_query_among_the_given_ids()
    {
        var sql = SqlRecorder.Start();
        IReadOnlyCollection<Guid> found;
        try
        {
            found = await UsersAsync(
                EventTarget.Users(u => u.Email!.StartsWith("a") && (u.HasPermission(IdentityPermissions.Users.View) || u.InGroup(_granted)) && !u.IsInRole("Nobody")),
                among: [_users["alice"], _users["bob"], _users["erin"]]);
        }
        finally
        {
            SqlRecorder.Stop();
        }

        Names(found).ShouldBe(["alice"]);
        var query = sql.Where(s => s.Contains("GroupMembers", StringComparison.OrdinalIgnoreCase)).ShouldHaveSingleItem();
        query.ShouldContain("LIKE", Case.Insensitive);
    }

    [Fact]
    public async Task A_predicate_the_database_cannot_run_fails_with_a_clear_message()
    {
        (await Should.ThrowAsync<InvalidOperationException>(() => UsersAsync(EventTarget.Users(u => Shout(u.Email!) == "ALICE"))))
            .Message.ShouldContain("cannot run in the database");
        (await Should.ThrowAsync<NotSupportedException>(() => UsersAsync(EventTarget.Users(u => u.IsInRole(u.Email!)))))
            .Message.ShouldContain("IsInRole");
        (await Should.ThrowAsync<NotSupportedException>(() => UsersAsync(EventTarget.Global))).Message.ShouldContain("realtime");
    }

    [Fact]
    public async Task A_custom_audience_only_narrows_to_active_users_of_the_organisation()
    {
        PartyAudience.Ids = [_users["alice"], _users["eve"], _users["ivan"]];
        Names(await UsersAsync(EventTarget.Custom<PartyAudience>("S-1"))).ShouldBe(["alice"]);
        PartyAudience.LastArgs.ShouldBe("S-1");
        Names(await UsersAsync(EventTarget.Custom<PartyAudience>() | EventTarget.Users(_users["gina"]))).ShouldBe(["alice", "gina"], ignoreOrder: true);
    }

    private static string Shout(string text) => text.ToUpperInvariant();

    private List<string> Names(IEnumerable<Guid> ids) => [.. ids.Select(id => _users.Single(u => u.Value == id).Key)];

    private async Task<IReadOnlyCollection<Guid>> UsersAsync(EventTarget target, Guid? actor = null, IReadOnlyCollection<Guid>? among = null)
    {
        using var current = CurrentUserScope.Begin(new ImpersonatedUser(actor ?? _setup.AdminUserId, _setup.TenantId));
        await using var scope = app.App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IEventTargetResolver>().UsersAsync(target, among, Ct);
    }

    private async Task<bool> CheckerGrantsAsync(Guid userId, string permission)
    {
        using var current = CurrentUserScope.Begin(new ImpersonatedUser(userId, _setup.TenantId));
        await using var scope = app.App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPermissionChecker>().IsGrantedAsync(permission, Ct);
    }

    private async Task PutAsync<T>(string url, T body) => (await Admin.PutAsJsonAsync(url, body, Ct)).EnsureSuccessStatusCode();

    private async Task<Guid> UserAsync(string name)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest($"{name}@acme.test", "Passw0rd!x", null, null), Ct);
        response.EnsureSuccessStatusCode();
        return _users[name] = (await response.Content.ReadFromJsonAsync<UserDto>(Ct))!.Id;
    }

    private async Task<Guid> RoleAsync(string name, params string[] permissions)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest(name, null), Ct);
        response.EnsureSuccessStatusCode();
        var id = await response.Content.ReadFromJsonAsync<Guid>(Ct);
        await PutAsync($"/api/v1/identity/permissions/grants/Role/{id}", new NameListRequest(permissions));
        return id;
    }

    private async Task<Guid> GroupAsync(string name, Guid? parent = null)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/identity/groups", new GroupRequest(name, null, parent), Ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Guid>(Ct);
    }
}

/// <summary>An app's audience: whatever ids it names, even of other organisations.</summary>
public sealed class PartyAudience : IAudienceResolver
{
    public static IReadOnlyCollection<Guid> Ids { get; set; } = [];

    public static object? LastArgs { get; private set; }

    public Task<IReadOnlyCollection<Guid>> ResolveAsync(object? args, CancellationToken cancellationToken)
    {
        LastArgs = args;
        return Task.FromResult(Ids);
    }
}
