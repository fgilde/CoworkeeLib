using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Tests;

public sealed class UserAdministrationTests(IdentityApp app) : IAsyncLifetime
{
    private const string Pixel = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync()
    {
        await app.ResetAllAsync();
        _setup = await app.SetupAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    private async Task<UserDto> CreateAsync(CreateUserRequest request)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/identity/users", request, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UserDto>(Ct))!;
    }

    private Task<UserDetailDto?> DetailAsync(Guid id) => Admin.GetFromJsonAsync<UserDetailDto>($"/api/v1/identity/users/{id}", Ct);

    [Fact]
    public async Task Creates_a_user_without_password_with_roles_and_inactive()
    {
        var roles = (await Admin.GetFromJsonAsync<RoleDto[]>("/api/v1/identity/roles", Ct))!;
        var userRole = roles.Single(r => r.Name == SystemRoles.User);

        var user = await CreateAsync(new CreateUserRequest(" eve@acme.test ", null, "Eve", null, RoleIds: [userRole.Id], IsActive: false));

        user.Roles.Select(r => r.Name).ShouldBe([SystemRoles.User]);
        var detail = (await DetailAsync(user.Id))!;
        (detail.Email, detail.IsActive, detail.HasPassword).ShouldBe(("eve@acme.test", false, false));
        (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest("x@acme.test", null, null, null, RoleIds: [Guid.CreateVersion7()]), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Edits_names_user_name_contact_and_status()
    {
        var user = await CreateAsync(new CreateUserRequest("bob@acme.test", "Passw0rd!x", "Bob", null));

        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{user.Id}", new UpdateUserRequest(
            "Robert", "Builder", true, true, "robert", " +49 1 ", new PostalAddress("Main 1", "12345", "Berlin", null), false), Ct)).EnsureSuccessStatusCode();

        var detail = (await DetailAsync(user.Id))!;
        (detail.UserName, detail.FirstName, detail.LastName, detail.PhoneNumber, detail.EmailConfirmed, detail.MustChangePassword)
            .ShouldBe(("robert", "Robert", "Builder", "+49 1", false, true));
        detail.Address.ShouldBe(new PostalAddress("Main 1", "12345", "Berlin", null));

        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{user.Id}", new UpdateUserRequest("Robert", "Builder", true, PhoneNumber: ""), Ct)).EnsureSuccessStatusCode();
        detail = (await DetailAsync(user.Id))!;
        (detail.UserName, detail.PhoneNumber, detail.Address?.City).ShouldBe(("robert", null, "Berlin"));
    }

    [Fact]
    public async Task A_taken_user_name_and_overlong_values_are_refused()
    {
        var user = await CreateAsync(new CreateUserRequest("bob@acme.test", "Passw0rd!x", null, null));

        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{user.Id}", new UpdateUserRequest(null, null, true, UserName: "admin@acme.test"), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{user.Id}", new UpdateUserRequest(null, null, true, PhoneNumber: new string('1', 51)), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Sets_a_password_that_signs_in_and_asks_for_a_change()
    {
        var user = await CreateAsync(new CreateUserRequest("bob@acme.test", null, null, null));

        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{user.Id}/password", new SetPasswordRequest("Brand#New123", true), Ct)).EnsureSuccessStatusCode();
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{user.Id}/password", new SetPasswordRequest("short", false), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var stored = await app.InDbAsync(null, db => db.Set<User>().SingleAsync(u => u.Id == user.Id));
        new PasswordHasher<User>().VerifyHashedPassword(stored, stored.PasswordHash!, "Brand#New123").ShouldNotBe(PasswordVerificationResult.Failed);
        stored.MustChangePassword.ShouldBeTrue();
    }

    [Fact]
    public async Task Resets_two_step_verification_and_removes_external_sign_ins()
    {
        var user = await CreateAsync(new CreateUserRequest("bob@acme.test", "Passw0rd!x", null, null));
        await app.InDbAsync(null, async db =>
        {
            (await db.Set<User>().SingleAsync(u => u.Id == user.Id)).TwoFactorEnabled = true;
            db.Add(new IdentityUserLogin<Guid> { UserId = user.Id, LoginProvider = "Google", ProviderKey = "g/1", ProviderDisplayName = "Google" });
            return 0;
        });
        (await DetailAsync(user.Id))!.Logins!.Select(l => l.ProviderKey).ShouldBe(["g/1"]);

        (await Admin.PostAsync($"/api/v1/identity/users/{user.Id}/two-factor/reset", null, Ct)).EnsureSuccessStatusCode();
        (await Admin.DeleteAsync($"/api/v1/identity/users/{user.Id}/logins?provider=Google&key={Uri.EscapeDataString("g/1")}", Ct)).EnsureSuccessStatusCode();

        var detail = (await DetailAsync(user.Id))!;
        detail.TwoFactorEnabled.ShouldBeFalse();
        detail.Logins.ShouldBeEmpty();
    }

    [Fact]
    public async Task Sets_groups_and_the_picture_of_a_user()
    {
        var user = await CreateAsync(new CreateUserRequest("bob@acme.test", "Passw0rd!x", null, null));
        var crew = await (await Admin.PostAsJsonAsync("/api/v1/identity/groups", new GroupRequest("Crew", null), Ct)).Content.ReadFromJsonAsync<Guid>(Ct);
        var staff = await (await Admin.PostAsJsonAsync("/api/v1/identity/groups", new GroupRequest("Staff", null), Ct)).Content.ReadFromJsonAsync<Guid>(Ct);

        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{user.Id}/groups", new IdListRequest([crew, staff]), Ct)).EnsureSuccessStatusCode();
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{user.Id}/groups", new IdListRequest([staff]), Ct)).EnsureSuccessStatusCode();
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{user.Id}/avatar", new SetAvatarRequest(Pixel), Ct)).EnsureSuccessStatusCode();

        var detail = (await DetailAsync(user.Id))!;
        detail.Groups.Select(g => g.Name).ShouldBe(["Staff"]);
        detail.HasAvatar.ShouldBeTrue();
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{user.Id}/groups", new IdListRequest([Guid.CreateVersion7()]), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{user.Id}/avatar", new SetAvatarRequest("data:text/plain;base64,aGk="), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Only_user_managers_edit_and_only_users_of_the_own_organisation()
    {
        var bob = await CreateAsync(new CreateUserRequest("bob@acme.test", "Passw0rd!x", null, null));
        var stranger = await app.InDbAsync(null, async db =>
        {
            var tenant = new Tenant { Name = "Other", Identifier = "other" };
            db.Add(tenant);
            var user = new User { TenantId = tenant.Id, UserName = "x@other.test", Email = "x@other.test", NormalizedEmail = "X@OTHER.TEST", NormalizedUserName = "X@OTHER.TEST" };
            db.Add(user);
            await Task.CompletedTask;
            return user.Id;
        });
        var asBob = app.As(bob.Id, _setup.TenantId);

        (await asBob.PutAsJsonAsync($"/api/v1/identity/users/{bob.Id}/password", new SetPasswordRequest("Brand#New123", false), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await asBob.PostAsync($"/api/v1/identity/users/{bob.Id}/two-factor/reset", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await asBob.PutAsJsonAsync($"/api/v1/identity/users/{bob.Id}/groups", new IdListRequest([]), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{stranger}/password", new SetPasswordRequest("Brand#New123", false), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{stranger}/avatar", new SetAvatarRequest(null), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{stranger}", new UpdateUserRequest("X", null, true), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
