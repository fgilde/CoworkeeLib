using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Users;
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
    }

    private async Task<TResult> AsBobFromAdminAsync<TResult>(Guid bob, IRequest<TResult> request)
    {
        using var actor = CurrentUserScope.Begin(new Impersonating(bob, _setup.TenantId, _setup.AdminUserId));
        await using var scope = app.App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(request, Ct);
    }

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
