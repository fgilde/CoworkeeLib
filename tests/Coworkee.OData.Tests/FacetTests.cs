using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;
using Coworkee.Contracts.Identity;

namespace Coworkee.OData.Tests;

/// <summary>Facets end to end: the client's selection builds the filter, the server answers rows and counts.</summary>
public sealed class FacetTests(ODataApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        await app.InDbAsync(_setup.TenantId, async db =>
        {
            db.AddRange(
                Make("Anvil", WidgetKind.Hammer, WidgetFeatures.Express | WidgetFeatures.Gift, true, 10, new(2026, 1, 15, 9, 0, 0, TimeSpan.Zero), "Red"),
                Make("Bolt", WidgetKind.Wrench, WidgetFeatures.Express, false, 25.5m, new(2026, 2, 10, 9, 0, 0, TimeSpan.Zero), "Blue"),
                Make("Clamp", WidgetKind.Saw, WidgetFeatures.Gift | WidgetFeatures.Insured, true, 40, new(2026, 2, 20, 9, 0, 0, TimeSpan.Zero), "Red"),
                Make("Drill", WidgetKind.Hammer, WidgetFeatures.None, true, 99, new(2026, 3, 5, 9, 0, 0, TimeSpan.Zero), "Green"));
            return await db.SaveChangesAsync();
        });
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task Enum_options_carry_member_names_display_labels_and_filters_by_name()
    {
        var (_, facets) = await QueryAsync(null);

        var kind = facets.Single(f => f.Key == "kind");
        kind.ValueKind.ShouldBe(FacetValueKind.Enum);
        kind.AllowExclude.ShouldBeTrue();
        kind.Options.Select(o => (o.Value.GetString(), o.Label, o.Count, o.OData)).ShouldBe(
            [("Hammer", "Claw hammer", 2, "Kind eq 'Hammer'"), ("Wrench", "Wrench", 1, "Kind eq 'Wrench'"), ("Saw", "Hand saw", 1, "Kind eq 'Saw'")], ignoreOrder: true);
        facets.Single(f => f.Key == "active").Options.Select(o => o.Label).ShouldBe(["Yes", "No"], ignoreOrder: true);
        (await QueryAsync("Kind eq 'Saw'")).Names.ShouldBe(["Clamp"]);
    }

    [Fact]
    public async Task Any_of_joins_with_or_and_keeps_counting_the_alternatives()
    {
        var (_, facets) = await QueryAsync(null);
        var kind = facets.Single(f => f.Key == "kind");
        var selection = new FacetSelection();

        selection.Toggle(kind, kind.Options.Single(o => o.Label == "Claw hammer"));
        selection.Toggle(kind, kind.Options.Single(o => o.Label == "Hand saw"));
        var (names, counted) = await QueryAsync(selection.ToFilter(), selection.Context(null, facets));
        var (_, narrowed) = await QueryAsync(selection.ToFilter());

        selection.ToFilter().ShouldBe("(Kind eq 'Hammer' or Kind eq 'Saw')");
        names.ShouldBe(["Anvil", "Clamp", "Drill"]);
        counted.Single(f => f.Key == "kind").Options.Single(o => o.Value.GetString() == "Wrench").Count.ShouldBe(1, "the own choice is left out of the group's counts");
        narrowed.Single(f => f.Key == "kind").Options.ShouldNotContain(o => o.Value.GetString() == "Wrench");
        counted.Single(f => f.Key == "active").Options.Sum(o => o.Count).ShouldBe(3, "other groups count with the choice");
    }

    [Fact]
    public async Task All_of_joins_flags_with_and_and_the_user_can_switch_to_any_of()
    {
        var (_, facets) = await QueryAsync(null);
        var features = facets.Single(f => f.Key == "features");
        var selection = new FacetSelection();

        features.ValueKind.ShouldBe(FacetValueKind.Flags);
        features.Combine.ShouldBe(FacetCombine.All);
        features.Switchable.ShouldBeTrue();
        features.Options.Select(o => (o.Value.GetString(), o.Count)).ShouldBe([("Express", 2), ("Gift", 2), ("Insured", 1)]);
        features.Options.Single(o => o.Label == "Insured").Meta!["color"].ShouldBe("#00838f");

        selection.Toggle(features, features.Options[0]);
        selection.Toggle(features, features.Options[1]);
        selection.ToFilter().ShouldBe("(Features has 'Express' and Features has 'Gift')");
        (await QueryAsync(selection.ToFilter(), selection.Context(null, facets))).Names.ShouldBe(["Anvil"]);

        selection.SetCombine(features, FacetCombine.Any);
        (await QueryAsync(selection.ToFilter())).Names.ShouldBe(["Anvil", "Bolt", "Clamp"]);
    }

    [Fact]
    public async Task Single_choice_keeps_one_value_and_exclusion_negates()
    {
        var (_, facets) = await QueryAsync(null);
        var color = facets.Single(f => f.Key == "color");
        var kind = facets.Single(f => f.Key == "kind");
        var selection = new FacetSelection();

        color.Combine.ShouldBe(FacetCombine.Single);
        color.View.ShouldBe(FacetView.Buttons, "ConfigureFacet sets the view");
        selection.Toggle(color, color.Options.Single(o => o.Label == "Red"));
        selection.Toggle(color, color.Options.Single(o => o.Label == "Blue"));
        selection.ToFilter().ShouldBe("Color eq 'Blue'");

        selection.Clear();
        selection.Toggle(kind, kind.Options.Single(o => o.Label == "Claw hammer"), exclude: true);
        selection.ToFilter().ShouldBe("not (Kind eq 'Hammer')");
        (await QueryAsync(selection.ToFilter())).Names.ShouldBe(["Bolt", "Clamp"]);

        selection.Toggle(kind, kind.Options.Single(o => o.Label == "Hand saw"));
        selection.ToFilter().ShouldBe("(Kind eq 'Saw' and not (Kind eq 'Hammer'))");
        (await QueryAsync(selection.ToFilter())).Names.ShouldBe(["Clamp"]);
    }

    [Fact]
    public async Task Ranges_come_with_bounds_and_a_histogram_of_buckets()
    {
        var (_, facets) = await QueryAsync(null);
        var price = facets.Single(f => f.Key == "price");
        var made = facets.Single(f => f.Key == "madeAt");
        var selection = new FacetSelection();

        (price.Min, price.Max).ShouldBe(("10", "99"));
        price.Options.Count.ShouldBe(4);
        price.Options.Sum(o => o.Count).ShouldBe(4);
        made.Options.Select(o => (o.Meta!["from"], o.Count)).ShouldBe([("2026-01-01", 1), ("2026-02-01", 2), ("2026-03-01", 1)]);
        (await QueryAsync(made.Options[1].OData)).Names.ShouldBe(["Bolt", "Clamp"]);

        selection.Set(price, "20 – 50", "(Price ge 20 and Price le 50)");
        var (names, counted) = await QueryAsync(selection.ToFilter(), selection.Context(null, facets));
        names.ShouldBe(["Bolt", "Clamp"]);
        counted.Single(f => f.Key == "price").Options.Sum(o => o.Count).ShouldBe(4, "a range counts the whole distribution");
    }

    [Fact]
    public async Task A_self_reference_named_Parent_counts_by_the_parent_name()
    {
        await app.InDbAsync(_setup.TenantId, async db =>
        {
            var widget = Make("Vise", WidgetKind.Saw, WidgetFeatures.None, true, 5, new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero), "Red");
            var frame = new Part { Name = "Frame", Widget = widget, TenantId = _setup.TenantId };
            db.AddRange(frame, new Part { Name = "Jaw", Widget = widget, Parent = frame, TenantId = _setup.TenantId },
                new Part { Name = "Screw", Widget = widget, Parent = frame, TenantId = _setup.TenantId });
            return await db.SaveChangesAsync();
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/odata/Parts");
        request.Headers.Add("Prefer", "odata.include-annotations=\"cn.facets\"");

        var response = await Admin.SendAsync(request, Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        response.IsSuccessStatusCode.ShouldBeTrue(body);
        var facets = JsonDocument.Parse(body).RootElement.GetProperty("@cn.facets").Deserialize<List<FacetGroupDto>>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var parent = facets.Single(f => f.Key == "parentId");
        parent.ValueKind.ShouldBe(FacetValueKind.Reference);
        parent.Options.Select(o => (o.Label, o.Count)).ShouldBe([("Frame", 2)]);
    }

    private async Task<(string[] Names, List<FacetGroupDto> Facets)> QueryAsync(string? filter, FacetContextDto? context = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/odata/Widgets?$orderby=Name" + (filter is null ? "" : "&$filter=" + Uri.EscapeDataString(filter)));
        request.Headers.Add("Prefer", "odata.include-annotations=\"cn.facets\"");
        if (context is not null)
        {
            request.Headers.Add(FacetContextDto.Header, context.Encode());
        }

        var response = await Admin.SendAsync(request, Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);
        response.IsSuccessStatusCode.ShouldBeTrue(body);
        var page = JsonDocument.Parse(body).RootElement;
        var names = page.GetProperty("value").EnumerateArray().Select(w => w.GetProperty("Name").GetString()!).ToArray();
        var facets = page.GetProperty("@cn.facets").Deserialize<List<FacetGroupDto>>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        return (names, facets);
    }

    private Widget Make(string name, WidgetKind kind, WidgetFeatures features, bool active, decimal price, DateTimeOffset madeAt, string color) =>
        new() { Name = name, Kind = kind, Features = features, Active = active, Price = price, MadeAt = madeAt, Color = color, TenantId = _setup.TenantId };
}
