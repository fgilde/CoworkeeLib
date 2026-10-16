using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Application.GlobalSearch;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Search;
using Coworkee.OData.GlobalSearch;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.OData.Tests;

public sealed class GlobalSearchODataTests(ODataApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        await app.InDbAsync(_setup.TenantId, async db =>
        {
            db.AddRange(
                new Gadget { Name = "Lamp", Category = "Light", TenantId = _setup.TenantId },
                new Gadget { Name = "Clamp", Category = "Tools", TenantId = _setup.TenantId },
                new Gadget { Name = "Lamp shade", Category = "Light", TenantId = _setup.TenantId },
                new Gadget { Name = "Lamp safe", Category = "Secret", TenantId = _setup.TenantId });
            return await db.SaveChangesAsync();
        });
        var other = Guid.CreateVersion7();
        await app.InDbAsync(other, async db =>
        {
            db.Add(new Gadget { Name = "Lamp of another organisation", Category = "Light", TenantId = other });
            return await db.SaveChangesAsync();
        });
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    private Task<GlobalSearchResult?> SearchAsync(HttpClient client, string text) =>
        client.GetFromJsonAsync<GlobalSearchResult>($"/api/v1/search?types=gadgets&q={Uri.EscapeDataString(text)}", Ct);

    [Fact]
    public async Task Finds_visible_rows_of_the_own_organisation_best_first()
    {
        var result = await SearchAsync(Admin, "lamp");

        result!.Hits.Select(h => h.Title).ShouldBe(["Lamp", "Lamp shade", "Clamp"]);
        result.Hits[0].ShouldSatisfyAllConditions(
            h => h.Score.ShouldBe(1),
            h => h.Subtitle.ShouldBe("Light"),
            h => h.EntitySet.ShouldBe("Gadgets"),
            h => h.Url!.ShouldStartWith("/gadgets?gadgets="),
            h => h.Highlights.ShouldBe([new HighlightRange(0, 4)]));
        result.Providers.ShouldBe([new GlobalSearchProviderStatus("gadgets", "Gadgets", GlobalSearchProviderState.Done, 3)]);
    }

    [Fact]
    public async Task Quotes_find_exact_values_and_an_id_finds_its_row()
    {
        (await SearchAsync(Admin, "\"lamp\""))!.Hits.Select(h => h.Title).ShouldBe(["Lamp"]);

        var clamp = (await SearchAsync(Admin, "clamp"))!.Hits.Single();
        (await SearchAsync(Admin, clamp.Id!))!.Hits.Select(h => h.Title).ShouldBe(["Clamp"]);
    }

    [Fact]
    public async Task Every_word_has_to_match_a_field()
    {
        (await SearchAsync(Admin, "tools clamp"))!.Hits.Select(h => h.Title).ShouldBe(["Clamp"]);
        (await SearchAsync(Admin, "light clamp"))!.Hits.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_stream_sends_prefix_matches_before_parts_and_ends_with_the_provider_state()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/search/stream?q=lamp&types=gadgets");
        using var response = await Admin.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Ct);

        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/x-ndjson");
        var lines = (await response.Content.ReadAsStringAsync(Ct)).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => JsonSerializer.Deserialize<GlobalSearchEvent>(l, JsonSerializerOptions.Web)!).ToList();
        lines.Where(l => l.Hit is not null).Select(l => l.Hit!.Title).ShouldBe(["Lamp", "Lamp shade", "Clamp"]);
        lines[^1].ShouldBe(new GlobalSearchEvent("gadgets", State: GlobalSearchProviderState.Done));
    }

    [Fact]
    public async Task Without_the_permission_of_the_set_its_provider_is_left_out()
    {
        var email = $"viewer-{Guid.NewGuid():N}@acme.test";
        var user = await (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null), Ct)).Content.ReadFromJsonAsync<UserDto>(Ct);
        var viewer = app.As(user!.Id, _setup.TenantId);

        (await SearchAsync(viewer, "lamp"))!.ShouldSatisfyAllConditions(r => r.Hits.ShouldBeEmpty(), r => r.Providers.ShouldBeEmpty());
        (await viewer.GetFromJsonAsync<List<GlobalSearchProviderDto>>("/api/v1/search/providers", Ct))!.Select(p => p.Name).ShouldNotContain("gadgets");
        (await Admin.GetFromJsonAsync<List<GlobalSearchProviderDto>>("/api/v1/search/providers", Ct))!.Select(p => p.Name).ShouldContain("gadgets");
    }

    [Fact]
    public async Task The_type_word_in_the_text_picks_the_provider()
    {
        var result = await Admin.GetFromJsonAsync<GlobalSearchResult>("/api/v1/search?q=gadget:clamp", Ct);

        result!.Hits.Select(h => h.Provider).Distinct().ShouldBe(["gadgets"]);
    }

    [Fact]
    public async Task The_users_of_the_organisation_are_found_too()
    {
        var result = await Admin.GetFromJsonAsync<GlobalSearchResult>("/api/v1/search?q=user:admin@acme", Ct);

        result!.Hits.ShouldContain(h => h.Provider == "users" && h.Url == $"/admin/users/{_setup.AdminUserId}");
    }

    [Fact]
    public void Searching_a_hidden_property_is_refused_at_start()
    {
        var services = new ServiceCollection()
            .AddODataEntity<Gadget>("Gadgets", null, g => g.SerialCode)
            .AddGlobalSearch<Gadget>(s => s.Fields(g => g.SerialCode))
            .BuildServiceProvider();

        Should.Throw<InvalidOperationException>(() => services.GetServices<IGlobalSearchProvider>().ToList()).Message.ShouldContain("SerialCode");
    }
}
