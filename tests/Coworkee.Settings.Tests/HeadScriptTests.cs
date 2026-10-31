using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Settings;
using Coworkee.Infrastructure.Auditing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Settings.Tests;

/// <summary>The head script (<c>AddCoworkeeHeadScript(perOrganisation: true)</c> in the test module): anonymous read, its own permission, organisations apart.</summary>
public sealed class HeadScriptTests(SettingsApp app) : IAsyncLifetime
{
    private const string Url = "/api/v1/settings/head-script";

    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task The_system_organisation_sets_the_installations_script_and_anyone_reads_it()
    {
        using var anonymous = app.App.GetTestClient();
        (await Admin.PutAsJsonAsync(Url, new SetHeadScriptRequest(true, "<script>window.tag = 1;</script>"), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await ReadAsync(anonymous)).ShouldBe(new HeadScriptDto(true, "<script>window.tag = 1;</script>"));
        // signed-in clients get it with the client settings too
        var client = (await Admin.GetFromJsonAsync<Dictionary<string, string?>>("/api/v1/settings/client", Ct))!;
        (client[HeadScriptSettings.Enabled], client[HeadScriptSettings.Value]).ShouldBe(("true", "<script>window.tag = 1;</script>"));
        // the change is audited, with who made it (the value itself is masked like every setting value)
        var row = (await app.InDbAsync(db => db.Set<SettingValue>().SingleAsync(v => v.Name == HeadScriptSettings.Value && v.Scope == SettingScope.Global, Ct))).Id.ToString();
        var audited = await app.InDbAsync(db => db.Set<AuditEntry>().Where(e => e.EntityType == nameof(SettingValue) && e.EntityId == row).ToListAsync(Ct));
        audited.ShouldContain(e => e.ActorId == _setup.AdminUserId && e.Changes.Any(c => c.Property == nameof(SettingValue.Value)));
    }

    [Fact]
    public async Task An_organisations_script_reaches_its_users_only()
    {
        (await Admin.PutAsJsonAsync(Url, new SetHeadScriptRequest(true, "<script>window.tag = 'all';</script>"), Ct)).EnsureSuccessStatusCode();
        var (user, tenant) = await app.CreateTenantAdminAsync();
        var other = app.As(user, tenant);

        (await other.PutAsJsonAsync(Url, new SetHeadScriptRequest(true, "<script>window.tag = 'other';</script>"), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await ReadAsync(other)).Value.ShouldBe("<script>window.tag = 'other';</script>");
        (await ReadAsync(Admin)).Value.ShouldBe("<script>window.tag = 'all';</script>");
        (await ReadAsync(app.App.GetTestClient())).Value.ShouldBe("<script>window.tag = 'all';</script>");
        var (third, thirdTenant) = await app.CreateTenantAdminAsync();
        (await ReadAsync(app.As(third, thirdTenant))).Value.ShouldBe("<script>window.tag = 'all';</script>");
        // the organisation cannot reach the installation's script, not even through the settings
        (await other.PutAsJsonAsync("/api/v1/settings/global", new SetSettingsRequest(new Dictionary<string, string?> { [HeadScriptSettings.Value] = "x" }), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Changing_it_needs_the_head_script_permission_also_through_the_settings()
    {
        var manager = await UserAsync(SettingsPermissions.Manage);

        (await manager.PutAsJsonAsync(Url, new SetHeadScriptRequest(true, "<script></script>"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        foreach (var name in new[] { HeadScriptSettings.Value, HeadScriptSettings.Enabled })
        {
            foreach (var scope in new[] { "global", "tenant" })
            {
                (await manager.PutAsJsonAsync($"/api/v1/settings/{scope}", new SetSettingsRequest(new Dictionary<string, string?> { [name] = "true" }), Ct))
                    .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            }
        }

        // other settings stay his
        (await manager.PutAsJsonAsync("/api/v1/settings/global", new SetSettingsRequest(new Dictionary<string, string?> { ["Test.Text"] = "x" }), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ReadAsync(Admin)).Enabled.ShouldBeFalse();
        (await app.App.GetTestClient().PutAsJsonAsync(Url, new SetHeadScriptRequest(true, "x"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var scripter = await UserAsync(SettingsPermissions.HeadScript);
        (await scripter.PutAsJsonAsync(Url, new SetHeadScriptRequest(true, new string('x', HeadScriptSettings.MaxLength + 1)), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await scripter.PutAsJsonAsync(Url, new SetHeadScriptRequest(false, "<script></script>"), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ReadAsync(Admin)).ShouldBe(new HeadScriptDto(false, "<script></script>"));
    }

    private async Task<HttpClient> UserAsync(string permission)
    {
        var email = $"u-{Guid.NewGuid():N}@acme.test";
        var user = (await (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null), Ct)).Content.ReadFromJsonAsync<UserDto>(Ct))!;
        var role = await (await Admin.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest("Role " + Guid.NewGuid().ToString("N")[..6], null), Ct)).Content.ReadFromJsonAsync<Guid>(Ct);
        (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{role}", new NameListRequest([permission]), Ct)).EnsureSuccessStatusCode();
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{user.Id}/roles", new IdListRequest([role]), Ct)).EnsureSuccessStatusCode();
        return app.As(user.Id, _setup.TenantId);
    }

    private static async Task<HeadScriptDto> ReadAsync(HttpClient client) => (await client.GetFromJsonAsync<HeadScriptDto>(Url, Ct))!;
}
