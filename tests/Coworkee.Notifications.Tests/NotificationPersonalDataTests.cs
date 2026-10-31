using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Notifications.Tests;

public sealed class NotificationPersonalDataTests(NotificationApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;
    private Guid _bob;
    private Guid _carl;

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        _bob = await CreateUserAsync("bob@acme.test");
        _carl = await CreateUserAsync("carl@acme.test");
        foreach (var user in new[] { _bob, _carl })
        {
            await app.InDbAsync(_setup.TenantId, async db =>
            {
                db.Add(new NotificationPreference { UserId = user, Type = TestNotificationModule.Eta, Scope = NotificationScope.All, InApp = true, Email = false });
                db.Add(new NotificationFollow { TenantId = _setup.TenantId, UserId = user, EntityType = "Shipments", EntityId = Guid.CreateVersion7(), Title = "S-1", CreatedAt = DateTimeOffset.UtcNow });
                db.Add(new PendingNotificationMail { UserId = user, TenantId = _setup.TenantId, Type = TestNotificationModule.Eta, Key = "Shipments:1", Title = "ETA moved", DueAt = DateTimeOffset.UtcNow });
                db.Add(new NotificationDigestState { UserId = user, LastSentAt = DateTimeOffset.UtcNow });
                return await db.SaveChangesAsync();
            });
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_export_holds_preferences_follows_pending_mails_and_the_digest_state()
    {
        var export = await app.As(_bob, _setup.TenantId).GetFromJsonAsync<JsonElement>("/api/v1/identity/me/personal-data", Ct);

        var section = export.GetProperty("notifications");
        section.GetProperty("preferences").EnumerateArray().Single().GetProperty("type").GetString().ShouldBe(TestNotificationModule.Eta);
        section.GetProperty("follows").EnumerateArray().Single().GetProperty("title").GetString().ShouldBe("S-1");
        section.GetProperty("pendingMails").EnumerateArray().Single().GetProperty("title").GetString().ShouldBe("ETA moved");
        section.GetProperty("digestLastSentAt").ValueKind.ShouldBe(JsonValueKind.String);
    }

    [Fact]
    public async Task Erasing_a_user_removes_his_preferences_follows_and_pending_mails_and_keeps_the_others()
    {
        (await app.As(_setup.AdminUserId, _setup.TenantId).DeleteAsync($"/api/v1/identity/users/{_bob}", Ct)).EnsureSuccessStatusCode();

        (await CountAsync(_bob)).ShouldBe((0, 0, 0, 0));
        (await CountAsync(_carl)).ShouldBe((1, 1, 1, 1));
    }

    [Fact]
    public async Task Erasing_a_user_removes_what_he_left_in_another_organisation()
    {
        var elsewhere = Guid.CreateVersion7();
        await app.InDbAsync(elsewhere, db =>
        {
            db.Add(new NotificationFollow { TenantId = elsewhere, UserId = _bob, EntityType = "Shipments", EntityId = Guid.CreateVersion7(), Title = "S-2", CreatedAt = DateTimeOffset.UtcNow });
            return db.SaveChangesAsync();
        });

        var export = await app.As(_bob, _setup.TenantId).GetFromJsonAsync<JsonElement>("/api/v1/identity/me/personal-data", Ct);
        export.GetProperty("notifications").GetProperty("follows").EnumerateArray().Select(f => f.GetProperty("title").GetString()).ShouldBe(["S-1", "S-2"], ignoreOrder: true);

        (await app.As(_setup.AdminUserId, _setup.TenantId).DeleteAsync($"/api/v1/identity/users/{_bob}", Ct)).EnsureSuccessStatusCode();

        (await app.InDbAsync(null, db => db.Set<NotificationFollow>().IgnoreQueryFilters().CountAsync(f => f.UserId == _bob, Ct))).ShouldBe(0);
    }

    private Task<(int, int, int, int)> CountAsync(Guid user) => app.InDbAsync(_setup.TenantId, async db => (
        await db.Set<NotificationPreference>().CountAsync(p => p.UserId == user, Ct),
        await db.Set<NotificationFollow>().CountAsync(f => f.UserId == user, Ct),
        await db.Set<PendingNotificationMail>().CountAsync(m => m.UserId == user, Ct),
        await db.Set<NotificationDigestState>().CountAsync(s => s.UserId == user, Ct)));

    private async Task<Guid> CreateUserAsync(string email) =>
        (await (await app.As(_setup.AdminUserId, _setup.TenantId).PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null), Ct))
            .Content.ReadFromJsonAsync<UserDto>(Ct))!.Id;
}
