using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Users;
using Coworkee.Identity.ApiKeys;
using Coworkee.Identity.Roles;
using Coworkee.Identity.Users.Privacy;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Identity.Tests;

public sealed class ImpersonationTests(IdentityApp app) : IAsyncLifetime
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
    public async Task A_ticket_needs_the_permission()
    {
        var bob = await UserWithAsync("bob@acme.test", IdentityPermissions.Users.View);
        var eve = await UserWithAsync("eve@acme.test");

        (await StartAsync(app.As(bob, _setup.TenantId), eve)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Nobody_signs_in_as_himself_an_inactive_user_or_one_of_another_tenant()
    {
        var inactive = await UserWithAsync("off@acme.test");
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{inactive}", new UpdateUserRequest(null, null, false), Ct)).EnsureSuccessStatusCode();
        var stranger = await app.InDbAsync(null, async db =>
        {
            var tenant = new Tenant { Name = "Other", Identifier = "other" };
            var user = new User { TenantId = tenant.Id, UserName = "eve@other.test", Email = "eve@other.test" };
            db.AddRange(tenant, user);
            await Task.CompletedTask;
            return user.Id;
        });

        var self = await StartAsync(Admin, _setup.AdminUserId);
        self.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeAsync(self)).ShouldBe("identity.impersonate_self");
        (await StartAsync(Admin, inactive)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await StartAsync(Admin, stranger)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await StartAsync(Admin, Guid.CreateVersion7())).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await StartAsync(Admin, Guid.Empty)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Only_users_without_more_permissions_than_the_caller()
    {
        var helper = await UserWithAsync("helper@acme.test", IdentityPermissions.Users.Impersonate, IdentityPermissions.Groups.View);
        var bob = await UserWithAsync("bob@acme.test", IdentityPermissions.Users.View);
        var boss = await UserWithAsync("boss@acme.test", IdentityPermissions.Users.Manage);
        var asHelper = app.As(helper, _setup.TenantId);

        (await StartAsync(asHelper, bob)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var more = await StartAsync(asHelper, boss);
        more.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(more)).ShouldBe("identity.impersonate_more_permissions");
        (await StartAsync(asHelper, _setup.AdminUserId)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await StartAsync(Admin, boss)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_target_acting_in_one_of_his_roles_still_counts_with_all_of_them()
    {
        var helper = await UserWithAsync("helper@acme.test", IdentityPermissions.Users.Impersonate, IdentityPermissions.Users.View);
        var bob = await UserWithAsync("bob@acme.test");
        var viewers = await RoleAsync("Viewers", IdentityPermissions.Users.View);
        var groups = await RoleAsync("Group viewers", IdentityPermissions.Groups.View);
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{bob}/roles", new IdListRequest([viewers, groups]), Ct)).EnsureSuccessStatusCode();
        (await app.As(bob, _setup.TenantId).PutAsJsonAsync("/api/v1/identity/roles/me/active", new SetActiveRoleRequest(viewers), Ct)).EnsureSuccessStatusCode();

        var response = await StartAsync(app.As(helper, _setup.TenantId), bob);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(response)).ShouldBe("identity.impersonate_more_permissions");
    }

    [Fact]
    public async Task A_locked_user_counts_as_not_found()
    {
        var bob = await UserWithAsync("bob@acme.test");
        (await Admin.PostAsJsonAsync($"/api/v1/identity/users/{bob}/lock", new LockUserRequest(null), Ct)).EnsureSuccessStatusCode();

        (await StartAsync(Admin, bob)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_support_user_of_one_group_cannot_sign_in_as_a_user_of_another()
    {
        var (helper, bob) = await HelperAndBobAsync();
        var a = await GroupAsync("A");
        var b = await GroupAsync("B");
        await MembersAsync(a, helper);
        await MembersAsync(b, bob);

        var response = await StartAsync(app.As(helper, _setup.TenantId), bob);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(response)).ShouldBe("identity.impersonate_other_groups");
    }

    [Fact]
    public async Task With_scope_all_a_support_user_signs_in_as_a_user_of_any_group()
    {
        var (helper, bob) = await HelperAndBobAsync(IdentityPermissions.Groups.ScopeAll);
        var a = await GroupAsync("A");
        var b = await GroupAsync("B");
        await MembersAsync(a, helper);
        await MembersAsync(b, bob);

        (await StartAsync(app.As(helper, _setup.TenantId), bob)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_member_of_a_parent_group_signs_in_as_a_member_of_a_child_group_but_not_the_other_way()
    {
        var (helper, bob) = await HelperAndBobAsync();
        var parent = await GroupAsync("Customers");
        var child = await GroupAsync("Acme", parent);
        await MembersAsync(parent, helper);
        await MembersAsync(child, bob);
        (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/User/{bob}", new NameListRequest([IdentityPermissions.Users.Impersonate]), Ct)).EnsureSuccessStatusCode();

        (await StartAsync(app.As(helper, _setup.TenantId), bob)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await StartAsync(app.As(bob, _setup.TenantId), helper)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_grant_on_a_resource_counts_unless_the_caller_holds_it_globally_or_on_the_same_resource()
    {
        var helper = await UserWithAsync("helper@acme.test", IdentityPermissions.Users.Impersonate);
        var bob = await UserWithAsync("bob@acme.test");
        var managers = await RoleAsync("Folder managers", IdentityPermissions.ResourcePermissions.Manage);
        var folder = Guid.CreateVersion7();
        await ResourceGrantAsync(folder, bob, managers);
        var asHelper = app.As(helper, _setup.TenantId);

        (await StartAsync(asHelper, bob)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        await ResourceGrantAsync(Guid.CreateVersion7(), helper, managers);
        (await StartAsync(asHelper, bob)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        await ResourceGrantAsync(folder, helper, managers);
        (await StartAsync(asHelper, bob)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await StartAsync(Admin, bob)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_ticket_names_both_users_for_a_minute()
    {
        var bob = await UserWithAsync("bob@acme.test");
        var tickets = app.App.Services.GetRequiredService<ImpersonationTickets>();

        var response = await StartAsync(Admin, bob);

        var ticket = (await response.Content.ReadFromJsonAsync<ImpersonationTicketDto>(Ct))!.Ticket;
        tickets.Read(ticket).ShouldBe(new ImpersonationTicket(_setup.AdminUserId, bob, _setup.TenantId));
        tickets.Read(ticket[..^4] + "AAAA").ShouldBeNull();
        tickets.Read("no ticket").ShouldBeNull();
        var issuedEarlier = new ImpersonationTickets(app.App.Services.GetRequiredService<IDataProtectionProvider>(), new ShiftedClock(TimeSpan.FromMinutes(-2)));
        tickets.Read(issuedEarlier.Issue(new ImpersonationTicket(_setup.AdminUserId, bob, _setup.TenantId))).ShouldBeNull();
    }

    [Fact]
    public async Task While_signed_in_as_another_user_neither_a_new_ticket_nor_own_credentials_and_data()
    {
        var bob = await UserWithAsync("bob@acme.test", IdentityPermissions.Users.Impersonate);
        var eve = await UserWithAsync("eve@acme.test");

        (await AsBobFromAdminAsync(bob, new StartImpersonation(eve))).Error!.Code.ShouldBe("identity.already_impersonating");
        (await AsBobFromAdminAsync(bob, new ExportMyPersonalData())).Error!.Code.ShouldBe("auth.impersonating");
        (await AsBobFromAdminAsync(bob, new DeleteMyAccount("bob@acme.test"))).Error!.Code.ShouldBe("auth.impersonating");
        (await AsBobFromAdminAsync(bob, new SetActiveRole(null))).Error!.Code.ShouldBe("auth.impersonating");
        (await AsBobFromAdminAsync(bob, new CreateApiKey(new CreateApiKeyRequest("key", null, DateTimeOffset.UtcNow.AddDays(1), [IdentityPermissions.Users.View])))).Error!.Code.ShouldBe("auth.impersonating");
    }

    private async Task<TResult> AsBobFromAdminAsync<TResult>(Guid bob, IRequest<TResult> request)
    {
        using var actor = CurrentUserScope.Begin(new Impersonating(bob, _setup.TenantId, _setup.AdminUserId));
        await using var scope = app.App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(request, Ct);
    }

    private async Task<(Guid Helper, Guid Bob)> HelperAndBobAsync(params string[] extra) =>
        (await UserWithAsync("helper@acme.test", [IdentityPermissions.Users.Impersonate, .. extra]), await UserWithAsync("bob@acme.test"));

    private async Task<Guid> GroupAsync(string name, Guid? parent = null)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/identity/groups", new GroupRequest(name, null, parent), Ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Guid>(Ct);
    }

    private async Task MembersAsync(Guid group, params Guid[] users) =>
        (await Admin.PutAsJsonAsync($"/api/v1/identity/groups/{group}/members", new IdListRequest(users), Ct)).EnsureSuccessStatusCode();

    private async Task<Guid> RoleAsync(string name, params string[] permissions)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest(name, null), Ct);
        response.EnsureSuccessStatusCode();
        var id = await response.Content.ReadFromJsonAsync<Guid>(Ct);
        (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{id}", new NameListRequest(permissions), Ct)).EnsureSuccessStatusCode();
        return id;
    }

    private async Task ResourceGrantAsync(Guid folder, Guid user, Guid role) =>
        (await Admin.PostAsJsonAsync($"/api/v1/identity/resource-permissions/Folder/{folder}", new GrantResourcePermissionRequest(PrincipalType.User, user, role), Ct))
        .EnsureSuccessStatusCode();

    private Task<HttpResponseMessage> StartAsync(HttpClient client, Guid userId) => client.PostAsync($"/api/v1/identity/users/{userId}/impersonation", null, Ct);

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString();

    private async Task<Guid> UserWithAsync(string email, params string[] permissions)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null), Ct);
        response.EnsureSuccessStatusCode();
        var user = (await response.Content.ReadFromJsonAsync<UserDto>(Ct))!.Id;
        if (permissions.Length > 0)
        {
            (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/User/{user}", new NameListRequest(permissions), Ct)).EnsureSuccessStatusCode();
        }

        return user;
    }

    private sealed record Impersonating(Guid? UserId, Guid? TenantId, Guid? ImpersonatorId) : ICurrentUser
    {
        public bool IsAuthenticated => true;

        public IReadOnlyCollection<string> Roles => [];
    }

    private sealed class ShiftedClock(TimeSpan shift) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + shift;
    }
}
