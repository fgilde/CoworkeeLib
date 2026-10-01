using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Contracts.Identity;

namespace Coworkee.Identity.Tests;

public sealed class SetupTests(IdentityApp app) : IAsyncLifetime
{
    public async ValueTask InitializeAsync() => await app.ResetAllAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Api_is_gated_until_setup()
    {
        var response = await app.As(Guid.CreateVersion7(), Guid.CreateVersion7()).GetAsync("/api/v1/identity/users", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("setup_required");
    }

    [Fact]
    public async Task Status_reports_state()
    {
        (await Status()).IsInitialized.ShouldBeFalse();
        await app.SetupAsync();
        (await Status()).IsInitialized.ShouldBeTrue();
    }

    [Fact]
    public async Task Wrong_token_is_forbidden()
    {
        var response = await Complete(new CompleteSetupRequest("wrong", "Acme", "admin@acme.test", "Admin#12345", null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Weak_password_is_rejected()
    {
        var response = await Complete(new CompleteSetupRequest(IdentityApp.SetupToken, "Acme", "admin@acme.test", "short", null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Admin_from_setup_has_every_permission()
    {
        var setup = await app.SetupAsync();

        var permissions = (await app.As(setup.AdminUserId, setup.TenantId).GetFromJsonAsync<string[]>("/api/v1/identity/permissions/me", Ct))!;

        permissions.ShouldContain(IdentityPermissions.Users.Manage);
        permissions.ShouldContain(IdentityPermissions.Permissions.Manage);
    }

    [Fact]
    public async Task Second_setup_conflicts()
    {
        await app.SetupAsync();

        var response = await Complete(new CompleteSetupRequest(IdentityApp.SetupToken, "Other", "other@acme.test", "Admin#12345", null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<SetupStatusDto> Status() =>
        (await app.Anonymous().GetFromJsonAsync<SetupStatusDto>("/api/v1/setup/status", Ct))!;

    private Task<HttpResponseMessage> Complete(CompleteSetupRequest request) =>
        app.Anonymous().PostAsJsonAsync("/api/v1/setup/complete", request, Ct);
}
