using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts.Features;
using Coworkee.Contracts.Identity;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Features.Tests;

public sealed class FeatureTests(FeaturesApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Host => app.As(_setup.AdminUserId, _setup.TenantId);

    private async Task<EditionDto> CreateEditionAsync(string name, Dictionary<string, string> values)
    {
        var response = await Host.PostAsJsonAsync("/api/v1/editions", new EditionRequest(name, null, values), Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EditionDto>(Ct))!;
    }

    private async Task<(Guid UserId, Guid TenantId)> TenantWithAsync(Guid? editionId, Dictionary<string, string>? overrides = null)
    {
        var other = await app.CreateTenantAdminAsync();
        (await Host.PutAsJsonAsync($"/api/v1/tenants/{other.TenantId}/features", new TenantFeaturesRequest(editionId, overrides), Ct)).EnsureSuccessStatusCode();
        return other;
    }

    private Task<Dictionary<string, string?>?> FeaturesOfAsync((Guid UserId, Guid TenantId) user) =>
        app.As(user.UserId, user.TenantId).GetFromJsonAsync<Dictionary<string, string?>>("/api/v1/features", Ct);

    [Fact]
    public async Task Without_edition_the_defaults_apply()
    {
        var features = await Host.GetFromJsonAsync<Dictionary<string, string?>>("/api/v1/features", Ct);

        features![TestFeatures.Reports].ShouldBe("false");
        features[TestFeatures.MaxProjects].ShouldBe("3");
    }

    [Fact]
    public async Task Tenants_inherit_the_edition_and_overrides_win()
    {
        var pro = await CreateEditionAsync("Pro", new() { [TestFeatures.Reports] = "true", [TestFeatures.MaxProjects] = "50" });
        var tenant = await TenantWithAsync(pro.Id, new() { [TestFeatures.MaxProjects] = "100" });

        var features = await FeaturesOfAsync(tenant);

        features![TestFeatures.Reports].ShouldBe("true");
        features[TestFeatures.MaxProjects].ShouldBe("100");
        features[TestFeatures.Export].ShouldBe("false");
    }

    [Fact]
    public async Task Changing_an_edition_reaches_its_tenants_at_once()
    {
        var basic = await CreateEditionAsync("Basic", new() { [TestFeatures.Reports] = "false" });
        var tenant = await TenantWithAsync(basic.Id);
        (await FeaturesOfAsync(tenant))![TestFeatures.Reports].ShouldBe("false");

        (await Host.PutAsJsonAsync($"/api/v1/editions/{basic.Id}", new EditionRequest("Basic", null, new Dictionary<string, string> { [TestFeatures.Reports] = "true" }), Ct))
            .EnsureSuccessStatusCode();

        (await FeaturesOfAsync(tenant))![TestFeatures.Reports].ShouldBe("true");
    }

    [Fact]
    public async Task Deleting_an_edition_falls_back_to_the_defaults()
    {
        var pro = await CreateEditionAsync("Pro", new() { [TestFeatures.Reports] = "true" });
        var tenant = await TenantWithAsync(pro.Id);

        (await Host.DeleteAsync($"/api/v1/editions/{pro.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await FeaturesOfAsync(tenant))![TestFeatures.Reports].ShouldBe("false");
    }

    [Fact]
    public async Task RequiresFeature_blocks_commands_and_endpoints_until_the_feature_is_on()
    {
        var tenant = await TenantWithAsync(null);
        var client = app.As(tenant.UserId, tenant.TenantId);
        (await client.GetAsync("/api/v1/test/report", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.GetAsync("/api/v1/test/export", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await Host.PutAsJsonAsync($"/api/v1/tenants/{tenant.TenantId}/features",
            new TenantFeaturesRequest(null, new Dictionary<string, string> { [TestFeatures.Reports] = "true", [TestFeatures.Export] = "true" }), Ct)).EnsureSuccessStatusCode();

        (await client.GetAsync("/api/v1/test/report", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/api/v1/test/export", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("Test.Reports", "maybe")]
    [InlineData("Test.MaxProjects", "many")]
    [InlineData("Test.Unknown", "true")]
    public async Task Invalid_values_are_rejected(string name, string value) =>
        (await Host.PostAsJsonAsync("/api/v1/editions", new EditionRequest("Broken", null, new Dictionary<string, string> { [name] = value }), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

    [Fact]
    public async Task Host_creates_a_tenant_with_its_first_admin_and_sees_the_user_count()
    {
        var response = await Host.PostAsJsonAsync("/api/v1/tenants",
            new CreateTenantRequest("Beta", "Beta", true, false, "admin@beta.test", "Admin#12345"), Ct);
        response.EnsureSuccessStatusCode();
        var id = await response.Content.ReadFromJsonAsync<Guid>(Ct);

        var details = await (await Host.PostAsJsonAsync("/api/v1/tenants/details", new IdListRequest([id]), Ct)).Content.ReadFromJsonAsync<TenantDetailsDto[]>(Ct);

        details!.Single().UserCount.ShouldBe(1);
        (await app.InDbAsync(db => db.Set<Coworkee.Identity.Domain.Tenant>().SingleAsync(t => t.Id == id, Ct))).Identifier.ShouldBe("beta");
        (await Host.PostAsJsonAsync("/api/v1/tenants", new CreateTenantRequest("Beta 2", "beta", true, false, null, null), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Host_deactivates_tenants_but_not_itself()
    {
        var other = await app.CreateTenantAdminAsync();

        (await Host.PutAsJsonAsync($"/api/v1/tenants/{other.TenantId}", new TenantRequest("Other", "other-x", false, false), Ct)).EnsureSuccessStatusCode();
        (await Host.PutAsJsonAsync($"/api/v1/tenants/{_setup.TenantId}", new TenantRequest("Host", "default", false, false), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await app.InDbAsync(db => db.Set<Coworkee.Identity.Domain.Tenant>().SingleAsync(t => t.Id == other.TenantId, Ct))).IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task Only_the_host_manages_tenants_and_editions()
    {
        var other = await app.CreateTenantAdminAsync();
        var client = app.As(other.UserId, other.TenantId);

        (await client.GetAsync("/api/v1/editions", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.PostAsJsonAsync("/api/v1/editions", new EditionRequest("Free", null, null), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.PutAsJsonAsync($"/api/v1/tenants/{other.TenantId}/features", new TenantFeaturesRequest(null, null), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
