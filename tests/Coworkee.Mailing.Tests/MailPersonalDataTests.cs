using System.Net.Http.Json;
using Coworkee.Contracts.Identity;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Mailing.Tests;

public sealed class MailPersonalDataTests(MailApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Erasing_deletes_the_mails_to_his_address_in_his_organisation_only()
    {
        var created = await app.As(_setup.AdminUserId, _setup.TenantId).PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest("bob@acme.test", "Passw0rd!x", null, null), Ct);
        var bob = (await created.Content.ReadFromJsonAsync<UserDto>(Ct))!.Id;
        var elsewhere = Guid.CreateVersion7();
        await app.InDbAsync(db =>
        {
            db.Add(OutgoingMail.Queue("Bob@acme.test", new RenderedMail("Welcome", "<p>Hi</p>"), "Test", _setup.TenantId, DateTimeOffset.UtcNow));
            db.Add(OutgoingMail.Queue("bob@acme.test", new RenderedMail("Other organisation", "<p>Hi</p>"), "Test", elsewhere, DateTimeOffset.UtcNow));
            return db.SaveChangesAsync(Ct);
        });

        (await app.As(_setup.AdminUserId, _setup.TenantId).DeleteAsync($"/api/v1/identity/users/{bob}", Ct)).EnsureSuccessStatusCode();

        (await app.InDbAsync(db => db.Set<OutgoingMail>().Where(m => m.To.ToLower() == "bob@acme.test").Select(m => m.Subject).ToListAsync(Ct)))
            .ShouldBe(["Other organisation"]);
    }
}
