using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Application.Privacy;
using Coworkee.Contracts.Identity;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Webhooks.Tests;

public sealed class WebhookPersonalDataTests(WebhooksApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;
    private Guid _bob;

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        var created = await app.As(_setup.AdminUserId, _setup.TenantId).PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest("bob@acme.test", "Passw0rd!x", null, null), Ct);
        _bob = (await created.Content.ReadFromJsonAsync<UserDto>(Ct))!.Id;
        await app.WithDbAsync(null, _setup.TenantId, db =>
        {
            db.AddRange(Endpoint("https://bob.test/hook", _bob), Endpoint("https://admin.test/hook", _setup.AdminUserId));
            return db.SaveChangesAsync(Ct);
        });
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_export_lists_the_endpoints_he_saved()
    {
        var section = (await app.As(_bob, _setup.TenantId).GetFromJsonAsync<JsonElement>("/api/v1/identity/me/personal-data", Ct)).GetProperty("webhooks");

        section.EnumerateArray().Single().GetProperty("url").GetString().ShouldBe("https://bob.test/hook");
    }

    [Fact]
    public async Task Erasing_switches_his_endpoints_off_without_his_id_and_leaves_the_others_running()
    {
        (await app.As(_setup.AdminUserId, _setup.TenantId).DeleteAsync($"/api/v1/identity/users/{_bob}", Ct)).EnsureSuccessStatusCode();

        var endpoints = await app.WithDbAsync(null, _setup.TenantId, db => db.Set<WebhookSubscription>().ToDictionaryAsync(s => s.Url, Ct));
        var his = endpoints["https://bob.test/hook"];
        (his.OwnerId, his.IsActive, his.DisabledAt.HasValue).ShouldBe((PersonalDataSubject.ErasedUserId, false, true));
        var admins = endpoints["https://admin.test/hook"];
        (admins.OwnerId, admins.IsActive).ShouldBe((_setup.AdminUserId, true));
    }

    private WebhookSubscription Endpoint(string url, Guid owner) =>
        new() { TenantId = _setup.TenantId, Url = url, ProtectedSecret = "x", Events = ["*"], OwnerId = owner };
}
