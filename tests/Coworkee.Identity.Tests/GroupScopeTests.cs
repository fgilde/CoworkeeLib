using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Coworkee.Application.Groups;
using Coworkee.Contracts.Data;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Realtime;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Identity.Tests;

public sealed class GroupScopeTests(IdentityApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync()
    {
        await app.ResetAllAsync();
        _setup = await app.SetupAsync();
        RecordingRealtimePublisher.Instance.Events.Clear();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task Empty_selection_means_every_accessible_group()
    {
        var (user, a, a1, b) = await MemberOfAAndBAsync();

        var (memberships, selected, active) = await ScopeAsync(user, async s => (await s.MembershipsAsync(Ct), await s.SelectedAsync(Ct), await s.ActiveAsync(Ct)));

        memberships.ShouldBe([a, b], ignoreOrder: true);
        selected.ShouldBeEmpty();
        active.ShouldBe([a, a1, b], ignoreOrder: true);
    }

    [Fact]
    public async Task Selection_narrows_to_the_picked_groups_and_their_subtree()
    {
        var (user, a, a1, b) = await MemberOfAAndBAsync();

        (await SelectAsync(user, a)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await ScopeAsync(user, s => s.SelectedAsync(Ct))).ShouldBe([a]);
        (await ScopeAsync(user, s => s.ActiveAsync(Ct))).ShouldBe([a, a1], ignoreOrder: true);

        (await SelectAsync(user)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await ScopeAsync(user, s => s.ActiveAsync(Ct))).ShouldBe([a, a1, b], ignoreOrder: true);
    }

    [Fact]
    public async Task Selecting_an_inaccessible_group_is_rejected()
    {
        var user = await UserAsync("u");
        var a = await GroupAsync("A");
        var b = await GroupAsync("B");
        await AddMemberAsync(a, user);

        (await SelectAsync(user, a, b)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await SelectAsync(user, Guid.CreateVersion7())).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await app.InDbAsync(null, db => Task.FromResult(db.Set<GroupSelection>().Count()))).ShouldBe(0);
        RecordingRealtimePublisher.Instance.Events.ShouldNotContain(e => e.Type == GroupSelectionEvents.Changed);
    }

    [Fact]
    public async Task Selection_outside_access_is_ignored()
    {
        var (user, a, a1, b) = await MemberOfAAndBAsync();
        await SelectAsync(user, a);

        (await Admin.PostAsJsonAsync($"/api/v1/identity/groups/{a}/members/remove", new IdListRequest([user]), Ct)).EnsureSuccessStatusCode();

        (await ScopeAsync(user, s => s.SelectedAsync(Ct))).ShouldBeEmpty();
        (await ScopeAsync(user, s => s.ActiveAsync(Ct))).ShouldBe([b]);

        // moved under a group of the user, the stored selection is accessible again
        (await Admin.PutAsJsonAsync($"/api/v1/identity/groups/{a}/parent", new MoveGroupRequest(b), Ct)).EnsureSuccessStatusCode();

        (await ScopeAsync(user, s => s.ActiveAsync(Ct))).ShouldBe([a, a1], ignoreOrder: true);
    }

    [Fact]
    public async Task ScopeAll_makes_every_group_accessible_and_selectable()
    {
        var operatorId = await UserWithAsync(IdentityPermissions.Groups.ScopeAll);
        var a = await GroupAsync("A");
        var a1 = await GroupAsync("A1", a);
        var a1x = await GroupAsync("A1x", a1);
        var b = await GroupAsync("B");

        (await ScopeAsync(operatorId, s => s.MembershipsAsync(Ct))).ShouldBeEmpty();
        (await ScopeAsync(operatorId, s => s.AccessibleAsync(Ct))).ShouldBe([a, a1, a1x, b], ignoreOrder: true);

        (await SelectAsync(operatorId, a1x)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await ScopeAsync(operatorId, s => s.ActiveAsync(Ct))).ShouldBe([a1x]);
        // the picker gets memberships, the selection and their ancestors; the rest it loads lazily
        (await MineAsync(operatorId)).Select(g => (g.Id, g.Selected)).ShouldBe([(a, false), (a1, false), (a1x, true)], ignoreOrder: true);
    }

    [Fact]
    public async Task Api_keys_and_jobs_ignore_the_selection()
    {
        var (user, a, a1, b) = await MemberOfAAndBAsync();
        await SelectAsync(user, a);
        var withKey = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ApiKeyDefaults.IdClaim, Guid.CreateVersion7().ToString())], "test")) };

        (await ScopeAsync(user, s => s.ActiveAsync(Ct))).ShouldBe([a, a1], ignoreOrder: true);
        (await ScopeAsync(user, s => s.ActiveAsync(Ct), withKey)).ShouldBe([a, a1, b], ignoreOrder: true);
        (await ScopeAsync(user, s => s.ActiveAsync(Ct), http: null)).ShouldBe([a, a1, b], ignoreOrder: true);
        (await ScopeAsync(user, s => s.SelectedAsync(Ct), http: null)).ShouldBeEmpty();

        using (GroupScope.Selection([b]))
        {
            (await ScopeAsync(user, s => s.ActiveAsync(Ct), http: null)).ShouldBe([b]);
        }
    }

    [Fact]
    public async Task Unnarrowed_returns_accessible()
    {
        var (user, a, a1, b) = await MemberOfAAndBAsync();
        await SelectAsync(user, a);

        IReadOnlyList<Guid> inside;
        using (GroupScope.Unnarrowed())
        {
            using (GroupScope.Unnarrowed())
            {
            }

            GroupScope.IsUnnarrowed.ShouldBeTrue();
            inside = await ScopeAsync(user, s => s.ActiveAsync(Ct));
        }

        inside.ShouldBe([a, a1, b], ignoreOrder: true);
        GroupScope.IsUnnarrowed.ShouldBeFalse();
        (await ScopeAsync(user, s => s.ActiveAsync(Ct))).ShouldBe([a, a1], ignoreOrder: true);
    }

    [Fact]
    public async Task One_scope_follows_the_switches_between_calls()
    {
        var (user, a, a1, b) = await MemberOfAAndBAsync();
        await SelectAsync(user, a);

        var (narrowed, unnarrowed, overridden, again) = await ScopeAsync(user, async s =>
        {
            var first = await s.ActiveAsync(Ct);
            IReadOnlyList<Guid> wide, picked;
            using (GroupScope.Unnarrowed())
            {
                wide = await s.ActiveAsync(Ct);
            }

            using (GroupScope.Selection([b]))
            {
                picked = await s.ActiveAsync(Ct);
            }

            return (first, wide, picked, await s.ActiveAsync(Ct));
        });

        narrowed.ShouldBe([a, a1], ignoreOrder: true);
        unnarrowed.ShouldBe([a, a1, b], ignoreOrder: true);
        overridden.ShouldBe([b]);
        again.ShouldBe([a, a1], ignoreOrder: true);
    }

    [Fact]
    public async Task Nodes_and_ancestors_are_tenant_bound()
    {
        var (user, a, a1, _) = await MemberOfAAndBAsync();
        var foreign = await app.InDbAsync(null, async db =>
        {
            var tenant = new Tenant { Name = "Other", Identifier = "other" };
            var group = new UserGroup { Name = "A", TenantId = tenant.Id, Code = "00001" };
            db.Add(tenant);
            db.Add(group);
            await Task.CompletedTask;
            return group.Id;
        });

        (await ScopeAsync(user, s => s.NodesAsync([a, a1, foreign], Ct))).Select(n => n.Id).ShouldBe([a, a1], ignoreOrder: true);
        (await ScopeAsync(user, s => s.AncestorsAsync(a1, Ct))).ShouldBe([new GroupNode(a, null, "00001", "A")]);
        (await ScopeAsync(user, s => s.AncestorsAsync(foreign, Ct))).ShouldBeEmpty();
        (await SelectAsync(user, foreign)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var operatorId = await UserWithAsync(IdentityPermissions.Groups.ScopeAll);
        (await ScopeAsync(operatorId, s => s.AccessibleAsync(Ct))).ShouldNotContain(foreign);
    }

    [Fact]
    public async Task Without_a_user_the_lists_are_empty_but_nodes_follow_the_tenant()
    {
        var (_, a, a1, _) = await MemberOfAAndBAsync();
        var nodeA = new GroupNode(a, null, "00001", "A");

        using (CurrentUserScope.BeginSystem(_setup.TenantId))
        {
            await using var scope = app.App.Services.CreateAsyncScope();
            var groups = scope.ServiceProvider.GetRequiredService<IGroupScope>();

            (await groups.AccessibleAsync(Ct)).ShouldBeEmpty();
            (await groups.ActiveAsync(Ct)).ShouldBeEmpty();
            (await groups.NodesAsync([a], Ct)).ShouldBe([nodeA]);
            (await groups.AncestorsAsync(a1, Ct)).ShouldBe([nodeA]);
        }

        using (CurrentUserScope.Begin(new ImpersonatedUser(null, null)))
        {
            await using var scope = app.App.Services.CreateAsyncScope();
            var groups = scope.ServiceProvider.GetRequiredService<IGroupScope>();

            (await groups.NodesAsync([a], Ct)).ShouldBeEmpty();
            (await groups.AncestorsAsync(a1, Ct)).ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task Another_acting_user_in_the_same_scope_gets_their_own_groups()
    {
        var (user, a, a1, b) = await MemberOfAAndBAsync();
        var other = await UserAsync("other");
        await AddMemberAsync(b, other);
        await SelectAsync(user, a);

        await using var scope = app.App.Services.CreateAsyncScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext();
        var groups = scope.ServiceProvider.GetRequiredService<IGroupScope>();
        try
        {
            using (CurrentUserScope.Begin(new ImpersonatedUser(user, _setup.TenantId)))
            {
                (await groups.ActiveAsync(Ct)).ShouldBe([a, a1], ignoreOrder: true);
            }

            using (CurrentUserScope.Begin(new ImpersonatedUser(other, _setup.TenantId)))
            {
                (await groups.AccessibleAsync(Ct)).ShouldBe([b]);
                (await groups.ActiveAsync(Ct)).ShouldBe([b]);
            }
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    [Fact]
    public async Task My_groups_flags_members_and_selection()
    {
        var (user, a, a1, b) = await MemberOfAAndBAsync();
        await SelectAsync(user, a1);

        var mine = await MineAsync(user);

        mine.Select(g => (g.Id, g.IsMember, g.Selected)).ShouldBe([(a, true, false), (a1, false, true), (b, true, false)], ignoreOrder: true);
        mine.Single(g => g.Id == a1).ShouldBe(new MyGroupDto(a1, a, "00001.00001", "A1", false, true));
    }

    [Fact]
    public async Task Selection_change_is_published_to_the_user_topic()
    {
        var (user, a, _, _) = await MemberOfAAndBAsync();

        await SelectAsync(user, a);

        RecordingRealtimePublisher.Instance.Events.ShouldContain(e =>
            e.TenantId == _setup.TenantId && e.Topic == $"user:{user}" && e.Type == GroupSelectionEvents.Changed);
    }

    [Fact]
    public async Task Partition_changes_with_the_selection()
    {
        var (user, a, _, b) = await MemberOfAAndBAsync();
        var client = app.As(user, _setup.TenantId);
        async Task<string> PartitionAsync() => (await client.GetFromJsonAsync<ClientEntityManifest>("/api/v1/client-entities", Ct))!.Partition;

        var none = await PartitionAsync();
        await SelectAsync(user, a);
        var withA = await PartitionAsync();
        await SelectAsync(user, b);
        var withB = await PartitionAsync();
        await SelectAsync(user);

        new[] { none, withA, withB }.Distinct().Count().ShouldBe(3);
        (await PartitionAsync()).ShouldBe(none);
    }

    /// <summary>A user in A and B; A has the sub-group A1.</summary>
    private async Task<(Guid User, Guid A, Guid A1, Guid B)> MemberOfAAndBAsync()
    {
        var user = await UserAsync("member");
        var a = await GroupAsync("A");
        var a1 = await GroupAsync("A1", a);
        var b = await GroupAsync("B");
        await AddMemberAsync(a, user);
        await AddMemberAsync(b, user);
        return (user, a, a1, b);
    }

    private static readonly HttpContext Interactive = new DefaultHttpContext();

    /// <summary>Runs <paramref name="action"/> on the scope of <paramref name="user"/>, inside a request (interactive unless given) or, with null, outside one.</summary>
    private Task<T> ScopeAsync<T>(Guid user, Func<IGroupScope, Task<T>> action) => ScopeAsync(user, action, Interactive);

    private async Task<T> ScopeAsync<T>(Guid user, Func<IGroupScope, Task<T>> action, HttpContext? http)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(user, _setup.TenantId));
        await using var scope = app.App.Services.CreateAsyncScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = http;
        try
        {
            return await action(scope.ServiceProvider.GetRequiredService<IGroupScope>());
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    private Task<HttpResponseMessage> SelectAsync(Guid user, params Guid[] groups) =>
        app.As(user, _setup.TenantId).PutAsJsonAsync("/api/v1/identity/groups/mine/selection", new IdListRequest(groups), Ct);

    private async Task<IReadOnlyList<MyGroupDto>> MineAsync(Guid user) =>
        (await app.As(user, _setup.TenantId).GetFromJsonAsync<List<MyGroupDto>>("/api/v1/identity/groups/mine", Ct))!;

    private async Task AddMemberAsync(Guid group, Guid user) =>
        (await Admin.PostAsJsonAsync($"/api/v1/identity/groups/{group}/members/add", new IdListRequest([user]), Ct)).EnsureSuccessStatusCode();

    private async Task<Guid> GroupAsync(string name, Guid? parent = null)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/identity/groups", new GroupRequest(name, null, parent), Ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Guid>(Ct);
    }

    private async Task<Guid> UserAsync(string name)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest($"{name}@example.com", "Passw0rd!x", null, null), Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UserDto>(Ct))!.Id;
    }

    private async Task<Guid> UserWithAsync(string permission)
    {
        var user = await UserAsync("operator");
        var role = await Admin.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest("Operators", null), Ct);
        var roleId = await role.Content.ReadFromJsonAsync<Guid>(Ct);
        (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{roleId}", new NameListRequest([permission]), Ct)).EnsureSuccessStatusCode();
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{user}/roles", new IdListRequest([roleId]), Ct)).EnsureSuccessStatusCode();
        return user;
    }
}

/// <summary>Stands in for the realtime module, which the identity test app does not load.</summary>
public sealed class RecordingRealtimePublisher : IRealtimePublisher
{
    public static readonly RecordingRealtimePublisher Instance = new();

    public ConcurrentQueue<(Guid? TenantId, string Topic, string Type)> Events { get; } = new();

    public Task PublishAsync(Guid? tenantId, string topic, string type, object payload, CancellationToken cancellationToken = default)
    {
        Events.Enqueue((tenantId, topic, type));
        return Task.CompletedTask;
    }
}
