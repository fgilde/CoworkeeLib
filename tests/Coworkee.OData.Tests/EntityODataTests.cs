using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.TestHost;

namespace Coworkee.OData.Tests;

public sealed class EntityODataTests(ODataApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        await app.InDbAsync(_setup.TenantId, async db =>
        {
            db.AddRange(
                new Gadget { Name = "Drill", Category = "Tools", Price = 99, TenantId = _setup.TenantId },
                new Gadget { Name = "Hammer", Category = "Tools", Price = 19, TenantId = _setup.TenantId },
                new Gadget { Name = "Lamp", Category = "Light", Price = 35, TenantId = _setup.TenantId });
            return await db.SaveChangesAsync();
        });
        var other = Guid.CreateVersion7();
        await app.InDbAsync(other, async db =>
        {
            db.Add(new Gadget { Name = "Foreign", Category = "Tools", Price = 1, TenantId = other });
            return await db.SaveChangesAsync();
        });
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task Filters_sorts_and_counts_within_the_tenant()
    {
        var page = await Admin.GetFromJsonAsync<JsonElement>("/odata/Gadgets?$filter=Category eq 'Tools'&$orderby=Price desc&$count=true", Ct);

        page.GetProperty("@odata.count").GetInt32().ShouldBe(2);
        page.GetProperty("value").EnumerateArray().Select(g => g.GetProperty("Name").GetString()).ShouldBe(["Drill", "Hammer"]);
    }

    [Fact]
    public async Task Facets_come_with_the_page_when_asked_for()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/odata/Gadgets");
        request.Headers.Add("Prefer", "odata.include-annotations=\"*\"");

        var page = await (await Admin.SendAsync(request, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);

        var facets = page.EnumerateObject().Single(p => p.Name.EndsWith("facets", StringComparison.OrdinalIgnoreCase)).Value;
        facets.ToString().ShouldContain("Tools");
        facets.ToString().ShouldContain("Light");
        facets.ToString().ShouldNotContain("Foreign");
    }

    [Fact]
    public async Task A_single_entity_comes_by_key()
    {
        var id = (await Admin.GetFromJsonAsync<JsonElement>("/odata/Gadgets?$filter=Name eq 'Lamp'", Ct)).GetProperty("value")[0].GetProperty("Id").GetGuid();

        (await Admin.GetFromJsonAsync<JsonElement>($"/odata/Gadgets({id})", Ct)).GetProperty("Name").GetString().ShouldBe("Lamp");
    }

    [Fact]
    public async Task Without_the_permission_the_set_is_forbidden()
    {
        var email = $"viewer-{Guid.NewGuid():N}@acme.test";
        var user = await (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null), Ct)).Content.ReadFromJsonAsync<UserDto>(Ct);

        (await app.As(user!.Id, _setup.TenantId).GetAsync("/odata/Gadgets", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await app.App.GetTestClient().GetAsync("/odata/Gadgets", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
