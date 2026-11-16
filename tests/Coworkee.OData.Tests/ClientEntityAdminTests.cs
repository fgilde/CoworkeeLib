using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts;
using Coworkee.Contracts.Data;
using Coworkee.Contracts.Identity;
using Coworkee.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;

namespace Coworkee.OData.Tests;

// the Local copies page: every set that could be mirrored, its settings with their sources, and overrides from the configuration that apply live
public sealed class ClientEntityAdminTests(ODataApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_list_holds_every_set_with_a_guid_id_and_what_decided_its_copy()
    {
        var sets = await AdminAsync(app.As(_setup.AdminUserId, _setup.TenantId));

        var gadgets = sets.Single(s => s.EntitySet == "Gadgets");
        var widgets = sets.Single(s => s.EntitySet == "Widgets");
        (gadgets.Copy, gadgets.Sources["Copy"], gadgets.MaxRows, gadgets.Override).ShouldBe((null, ClientEntitySettingSource.Attribute, 50_000, null));
        (widgets.Copy, widgets.Sources["Copy"]).ShouldBe((ClientEntityCopy.Off, ClientEntitySettingSource.Default));
        // Ranks are keyed by a number: they cannot be mirrored
        sets.ShouldNotContain(s => s.EntitySet == "Ranks");
    }

    [Fact]
    public async Task Without_the_settings_permission_the_list_is_forbidden()
    {
        var admin = app.As(_setup.AdminUserId, _setup.TenantId);
        var email = $"viewer-{Guid.NewGuid():N}@acme.test";
        var user = await (await admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null), Ct)).Content.ReadFromJsonAsync<UserDto>(Ct);

        (await app.As(user!.Id, _setup.TenantId).GetAsync("/api/v1/client-entities/admin", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_override_changes_the_manifest_and_the_feed_live()
    {
        await using var other = await app.StartAnotherAsync(new Dictionary<string, string> { ["Coworkee:ClientEntities:Sets:Widgets:Copy"] = "Sqlite" });
        var client = other.GetTestClient().AsUser(_setup.AdminUserId, _setup.TenantId);
        var manifest = await client.GetFromJsonAsync<ClientEntityManifest>("/api/v1/client-entities", CoworkeeJson.Web, Ct);
        manifest!.Sets.Single(s => s.EntitySet == "Widgets").ShouldSatisfyAllConditions(s => s.Copy.ShouldBe(ClientEntityCopy.Sqlite), s => s.DefaultMode.ShouldBe(ClientEntityDefaultMode.Client));
        manifest.Sets.Single(s => s.EntitySet == "Gadgets").Copy.ShouldBeNull();
        (await client.GetAsync("/api/v1/client-entities/Widgets/ids", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // what saving on the page does: the database configuration changes and reloads
        var configuration = (IConfigurationRoot)other.Configuration;
        configuration["Coworkee:ClientEntities:Sets:Gadgets:Copy"] = "Off";
        configuration["Coworkee:ClientEntities:Sets:Widgets:DefaultMode"] = "Server";
        configuration["Coworkee:ClientEntities:Sets:Widgets:MaxRows"] = "123";
        configuration.Reload();

        var changed = await client.GetFromJsonAsync<ClientEntityManifest>("/api/v1/client-entities", CoworkeeJson.Web, Ct);
        changed!.Sets.ShouldNotContain(s => s.EntitySet == "Gadgets");
        changed.Sets.Single(s => s.EntitySet == "Widgets").ShouldSatisfyAllConditions(s => s.DefaultMode.ShouldBe(ClientEntityDefaultMode.Server), s => s.MaxRows.ShouldBe(123));
        (await client.GetAsync("/api/v1/client-entities/Gadgets/changes", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var widgets = (await AdminAsync(client)).Single(s => s.EntitySet == "Widgets");
        (widgets.Copy, widgets.Sources["Copy"], widgets.Sources["MaxRows"], widgets.Override!.MaxRows).ShouldBe((ClientEntityCopy.Sqlite, ClientEntitySettingSource.Override, ClientEntitySettingSource.Override, 123));
    }

    private static async Task<IReadOnlyList<ClientEntityAdminSetDto>> AdminAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<ClientEntityAdminSetDto[]>("/api/v1/client-entities/admin", CoworkeeJson.Web, Ct))!;
}
