using System.Net.Http.Json;
using Coworkee.Application.Privacy;
using Coworkee.Contracts.Identity;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Retention.Tests;

/// <summary>An app with most modules: the erase runs through all their contributors.</summary>
public sealed class RetentionPersonalDataTests(RetentionApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Erasing_keeps_the_runs_he_started_without_his_id()
    {
        var admin = app.As(_setup.AdminUserId, _setup.TenantId);
        var bob = (await (await admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest("bob@acme.test", "Passw0rd!x", null, null), Ct))
            .Content.ReadFromJsonAsync<UserDto>(Ct))!.Id;
        await app.InDbAsync(null, db =>
        {
            db.AddRange(new RetentionRun { Key = "by-bob", LastRunBy = bob }, new RetentionRun { Key = "by-admin", LastRunBy = _setup.AdminUserId });
            return db.SaveChangesAsync(Ct);
        });

        (await admin.DeleteAsync($"/api/v1/identity/users/{bob}", Ct)).EnsureSuccessStatusCode();

        var runs = await app.InDbAsync(null, db => db.Set<RetentionRun>().Where(r => r.Key.StartsWith("by-")).ToDictionaryAsync(r => r.Key, r => r.LastRunBy, Ct));
        runs["by-bob"].ShouldBe(PersonalDataSubject.ErasedUserId);
        runs["by-admin"].ShouldBe(_setup.AdminUserId);
    }
}
