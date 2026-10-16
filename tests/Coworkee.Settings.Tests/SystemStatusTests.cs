using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Settings;

namespace Coworkee.Settings.Tests;

public sealed class SystemStatusTests(SettingsApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_status_shows_versions_database_and_outbox()
    {
        var status = (await app.As(_setup.AdminUserId, _setup.TenantId).GetFromJsonAsync<SystemStatusDto>("/api/v1/system/status", Ct))!;

        status.Sections[0].Key.ShouldBe("versions");
        var database = status.Sections.Single(s => s.Key == "database");
        database.Entries.Select(e => e.Label).ShouldBe(["Provider", "Server version", "Pending migrations", "Outbox backlog", "Outbox messages with errors"]);
        database.Entries.Single(e => e.Label == "Server version").Value.ShouldNotBeNullOrWhiteSpace();
        status.Sections.Single(s => s.Key == "versions").Entries.Single(e => e.Label == ".NET").Value.ShouldStartWith(".NET");
    }

    [Fact]
    public async Task The_status_is_shown_in_the_system_organisation_only()
    {
        var (otherAdmin, otherTenant) = await app.CreateTenantAdminAsync();

        (await app.As(otherAdmin, otherTenant).GetAsync("/api/v1/system/status", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
