using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Infrastructure.Auditing;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Tests;

public sealed class PersonalDataTests(IdentityApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;
    private UserDto _bob = null!;

    public async ValueTask InitializeAsync()
    {
        await app.ResetAllAsync();
        _setup = await app.SetupAsync();
        var created = await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest("bob@acme.test", "Passw0rd!x", "Bob", "Builder"), Ct);
        _bob = (await created.Content.ReadFromJsonAsync<UserDto>(Ct))!;
        (await Bob.PutAsJsonAsync("/api/v1/identity/me", new UpdateProfileRequest("Bob", "Builder", "+49 1", new PostalAddress("Main St 1", "10115", "Berlin", "DE")), Ct))
            .EnsureSuccessStatusCode();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    private HttpClient Bob => app.As(_bob.Id, _setup.TenantId);

    [Fact]
    public async Task The_export_contains_the_profile_with_address()
    {
        var export = await Bob.GetFromJsonAsync<JsonElement>("/api/v1/identity/me/personal-data", Ct);

        var profile = export.GetProperty("profile");
        profile.GetProperty("email").GetString().ShouldBe("bob@acme.test");
        profile.GetProperty("address").GetProperty("city").GetString().ShouldBe("Berlin");
    }

    [Fact]
    public async Task Deleting_the_own_account_needs_the_address_and_leaves_only_a_blank_audit_trail()
    {
        (await Bob.PostAsJsonAsync("/api/v1/identity/me/delete", new DeleteAccountRequest("someone@acme.test"), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await Bob.PostAsJsonAsync("/api/v1/identity/me/delete", new DeleteAccountRequest(" BOB@acme.test "), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await Admin.GetAsync($"/api/v1/identity/users/{_bob.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var trail = await app.InDbAsync(null, db => db.Set<AuditEntry>().Where(e => e.EntityType == "User" && e.EntityId == _bob.Id.ToString()).ToListAsync(Ct));
        trail.ShouldNotBeEmpty();
        trail.SelectMany(e => e.Changes).ShouldAllBe(c => (c.OldValue == null || c.OldValue == "\"***\"") && (c.NewValue == null || c.NewValue == "\"***\""));
    }

    [Fact]
    public async Task Administrators_delete_users_but_never_the_last_administrator()
    {
        (await Admin.PostAsJsonAsync("/api/v1/identity/me/delete", new DeleteAccountRequest("admin@acme.test"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Bob.DeleteAsync($"/api/v1/identity/users/{_setup.AdminUserId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await Admin.DeleteAsync($"/api/v1/identity/users/{_bob.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await Admin.GetAsync($"/api/v1/identity/users/{_bob.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
