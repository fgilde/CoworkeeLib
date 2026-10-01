using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Settings;
using Coworkee.Infrastructure.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Settings.Tests;

public sealed class SettingsTests(SettingsApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task Default_applies_without_values() =>
        (await ClientValuesAsync(Admin))["Test.Text"].ShouldBe("fallback");

    [Fact]
    public async Task Tenant_overrides_global_and_user_overrides_tenant()
    {
        await PutAsync(Admin, "global", "Test.Text", "g");
        await PutAsync(Admin, "tenant", "Test.Text", "t");
        await PutAsync(Admin, "user", "Test.Text", "u");
        (await ClientValuesAsync(Admin))["Test.Text"].ShouldBe("u");

        await PutAsync(Admin, "user", "Test.Text", null);

        (await ClientValuesAsync(Admin))["Test.Text"].ShouldBe("t");
    }

    [Fact]
    public async Task Other_tenant_does_not_see_tenant_value()
    {
        await PutAsync(Admin, "global", "Test.Text", "g");
        await PutAsync(Admin, "tenant", "Test.Text", "t");

        (await ClientValuesAsync(app.As(Guid.CreateVersion7(), Guid.CreateVersion7())))["Test.Text"].ShouldBe("g");
    }

    [Fact]
    public async Task User_cannot_set_global_only_setting() =>
        (await SendAsync(Admin, "user", "Test.GlobalOnly", "1")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

    [Fact]
    public async Task Unknown_setting_is_rejected() =>
        (await SendAsync(Admin, "global", "Nope", "x")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

    [Fact]
    public async Task Int_setting_rejects_non_numbers() =>
        (await SendAsync(Admin, "global", "Test.GlobalOnly", "abc")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

    [Fact]
    public async Task Without_manage_permission_global_is_forbidden()
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest("bob@acme.test", "Passw0rd!x", null, null), Ct);
        var bob = (await response.Content.ReadFromJsonAsync<UserDto>(Ct))!;

        (await SendAsync(app.As(bob.Id, _setup.TenantId), "global", "Test.Text", "x")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Configured_default_overrides_definition_default() =>
        (await app.AsActorAsync(_setup.AdminUserId, _setup.TenantId, sp => sp.GetRequiredService<ISettingProvider>().GetAsync<int>("Test.Configured", Ct))).ShouldBe(7);

    [Fact]
    public async Task Secret_is_never_returned_in_plain_text()
    {
        await PutAsync(Admin, "global", "Test.Secret", "p@ss-word");

        var json = await Admin.GetStringAsync("/api/v1/settings/global", Ct);

        json.ShouldNotContain("p@ss-word");
        var secret = (await Admin.GetFromJsonAsync<SettingValueDto[]>("/api/v1/settings/global", Ct))!.Single(v => v.Name == "Test.Secret");
        secret.HasValue.ShouldBeTrue();
        secret.Value.ShouldBeNull();
    }

    [Fact]
    public async Task Secret_is_stored_encrypted_and_masked_in_audit()
    {
        await PutAsync(Admin, "global", "Test.Secret", "p@ss-word");

        var stored = await app.InDbAsync(db => db.Set<SettingValue>().Where(v => v.Name == "Test.Secret").Select(v => v.Value).SingleAsync(Ct));
        stored.ShouldNotBeNullOrEmpty();
        stored.ShouldNotContain("p@ss-word");
        var audited = await app.InDbAsync(db => db.Set<AuditEntry>().Where(e => e.EntityType == nameof(SettingValue)).ToListAsync(Ct));
        audited.ShouldNotBeEmpty();
        audited.SelectMany(e => e.Changes).ShouldAllBe(c => c.NewValue != "p@ss-word" && c.NewValue != stored);
    }

    [Fact]
    public async Task Provider_decrypts_secret()
    {
        await PutAsync(Admin, "global", "Test.Secret", "p@ss-word");

        (await app.AsActorAsync(_setup.AdminUserId, _setup.TenantId, sp => sp.GetRequiredService<ISettingProvider>().GetAsync("Test.Secret", Ct))).ShouldBe("p@ss-word");
    }

    [Fact]
    public async Task Secret_is_not_exposed_to_client()
    {
        await PutAsync(Admin, "global", "Test.Secret", "p@ss-word");

        (await ClientValuesAsync(Admin)).ShouldNotContainKey("Test.Secret");
    }

    [Fact]
    public async Task Definitions_are_grouped() =>
        (await Admin.GetFromJsonAsync<SettingGroupDto[]>("/api/v1/settings/definitions", Ct))!
            .Single(g => g.Name == "Test").Settings.Select(s => s.Name).ShouldContain("Test.Secret");

    [Fact]
    public async Task User_definitions_list_only_user_scoped_settings() =>
        (await Admin.GetFromJsonAsync<SettingGroupDto[]>("/api/v1/settings/definitions/user", Ct))!
            .SelectMany(g => g.Settings).Select(s => s.Name).ShouldBe(["Test.Text"]);

    private static async Task<Dictionary<string, string?>> ClientValuesAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<Dictionary<string, string?>>("/api/v1/settings/client", Ct))!;

    private static async Task PutAsync(HttpClient client, string scope, string name, string? value) =>
        (await SendAsync(client, scope, name, value)).EnsureSuccessStatusCode();

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string scope, string name, string? value) =>
        client.PutAsJsonAsync($"/api/v1/settings/{scope}", new SetSettingsRequest(new Dictionary<string, string?> { [name] = value }), Ct);
}
