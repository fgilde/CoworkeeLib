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

    [Fact]
    public async Task The_search_fields_of_the_sets_the_user_may_read_are_listed_for_the_data_tables()
    {
        var email = $"fields-{Guid.NewGuid():N}@acme.test";
        var user = await (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null), Ct)).Content.ReadFromJsonAsync<UserDto>(Ct);

        var fields = await Admin.GetFromJsonAsync<Dictionary<string, List<string>>>("/api/v1/search/fields", Ct);
        var others = await app.As(user!.Id, _setup.TenantId).GetFromJsonAsync<Dictionary<string, List<string>>>("/api/v1/search/fields", Ct);

        fields!["Gadgets"].ShouldBe(["Name", "Category"]);
        fields["Crates"].ShouldBe(["Name"]);
        fields.ShouldNotContainKey("Widgets", "no declared fields: every string property is $search's default, not the table's");
        others!.ShouldNotContainKey("Gadgets");
    }

    [Fact]
    public async Task The_providers_tell_how_to_search_a_local_copy()
    {
        var providers = await Admin.GetFromJsonAsync<List<GlobalSearchProviderDto>>("/api/v1/search/providers", Ct);

        var gadgets = providers!.Single(p => p.Name == "gadgets").Local.ShouldNotBeNull();
        (gadgets.EntitySet, string.Join(",", gadgets.Fields), gadgets.TitleField, gadgets.SubtitleField, gadgets.Url, gadgets.Page).ShouldBe(("Gadgets", "Name,Category", "Name", "Category", null, "/gadgets"));
        providers.Single(p => p.Name == "users").Local.ShouldBeNull("its title joins two names");
    }

    public static TheoryData<string> Rebuildable() => ["link", "page", "title", "navigation"];

    [Theory]
    [MemberData(nameof(Rebuildable))]
    public void A_client_rebuilds_hits_whose_title_subtitle_and_link_read_properties(string kind)
    {
        var local = Provider<Tray>(kind switch
        {
            "link" => s => s.Fields(t => t.Name).Subtitle(t => t.Name).Url(t => $"/trays/{t.Id}?bin={t.BinId}"),
            "page" => s => s.Fields(t => t.Name).Page("/trays"),
            "title" => s => s.Fields(t => t.Name).Title(t => t.Name).Url(t => "/trays/" + t.Id),
            _ => s => s.Fields(t => t.Bin!.Name, t => t.Name),
        }).Local.ShouldNotBeNull();

        local.EntitySet.ShouldBe("Trays");
        $"{string.Join(",", local.Fields)}|{local.TitleField}|{local.SubtitleField}|{local.Url}|{local.Page}".ShouldBe(kind switch
        {
            "link" => "Name|Name|Name|/trays/{Id}?bin={BinId}|",
            "page" => "Name|Name|||/trays",
            "title" => "Name|Name||/trays/{Id}|",
            _ => "Bin/Name,Name|Bin/Name|||",
        });
    }

    public static TheoryData<string> NotRebuildable() => ["joined title", "computed field", "escaped link", "navigation in the link", "formatted id"];

    [Theory]
    [MemberData(nameof(NotRebuildable))]
    public void Hits_a_client_cannot_rebuild_stay_with_the_server(string kind) =>
        Provider<Tray>(kind switch
        {
            "joined title" => s => s.Fields(t => t.Name).Title(t => t.Name + "!"),
            "computed field" => s => s.Fields(t => t.Name + t.Name),
            "escaped link" => s => s.Fields(t => t.Name).Url(t => "/trays?q=" + Uri.EscapeDataString(t.Name)),
            "navigation in the link" => s => s.Fields(t => t.Name).Url(t => $"/bins/{t.Bin!.Id}"),
            _ => s => s.Fields(t => t.Name).Url(t => $"/trays/{t.Id:N}"),
        }).Local.ShouldBeNull();

    [Fact]
    public void A_global_search_without_fields_searches_those_the_set_declares()
    {
        var services = new ServiceCollection()
            .AddGlobalSearch<Gadget>(s => s.Page("/gadgets"))
            .AddODataEntity<Gadget>("Gadgets", null, o => o.SearchFields(g => g.Name, g => g.SerialCode))
            .BuildServiceProvider();

        services.GetServices<IGlobalSearchProvider>().Single().Local!.Fields.ShouldBe(["Name", "SerialCode"]);
        Should.Throw<InvalidOperationException>(() => new ServiceCollection().AddODataEntity<Gadget>("Gadgets").AddGlobalSearch<Gadget>(s => s.Page("/g"))
            .BuildServiceProvider().GetServices<IGlobalSearchProvider>().ToList()).Message.ShouldContain("SearchFields");
    }

    private static IGlobalSearchProvider Provider<T>(Action<ODataSearchOptions<T>> configure)
        where T : class =>
        new ServiceCollection().AddODataEntity<T>(typeof(T).Name + "s").AddGlobalSearch(configure).BuildServiceProvider().GetServices<IGlobalSearchProvider>().Single();
}
