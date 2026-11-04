using System.Text.Json;
using Bunit;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Coworkee.Client.Blazor.Tests;

public sealed class FacetDisplayTests : ClientTestBase
{
    private readonly FacetSelection _selection = new();

    private static FacetGroupDto Countries => new("country", "Country", "Country", FacetKind.CheckboxList, FacetOperator.Or,
        [Option("DE", "Germany", 3), Option("FR", "FR", 1), Option("??", "Atlantis", 1)], true, 0) { ValueKind = FacetValueKind.Text, ValuePath = "Country", Display = FacetDisplayAttribute.Country };

    private static FacetOptionDto Option(string value, string label, int count) =>
        new(JsonSerializer.SerializeToElement(value), label, true, count, $"Country eq '{value}'");

    [Fact]
    public void Country_options_show_their_flag_before_the_label_in_every_view()
    {
        var list = Render<FacetCheckboxView>(p => p.Add(v => v.Group, Countries).Add(v => v.Selection, _selection));

        list.Find("[data-option=\"Country eq 'DE'\"] img.cw-flag").GetAttribute("src").ShouldEndWith("/de.svg");
        list.Find("[data-option=\"Country eq 'DE'\"]").TextContent.ShouldContain("Germany");
        list.Find("[data-option=\"Country eq 'FR'\"] img.cw-flag").GetAttribute("src").ShouldEndWith("/fr.svg");
        list.FindAll("[data-option=\"Country eq '??'\"] img").ShouldBeEmpty("an unknown country shows its label only");
        list.Find("[data-option=\"Country eq '??'\"]").TextContent.ShouldContain("Atlantis");

        Render<FacetChipView>(p => p.Add(v => v.Group, Countries).Add(v => v.Selection, _selection))
            .Find("[data-option=\"Country eq 'DE'\"] img.cw-flag").ShouldNotBeNull();
        Render<FacetButtonView>(p => p.Add(v => v.Group, Countries).Add(v => v.Selection, _selection))
            .Find("[data-option=\"Country eq 'DE'\"] img.cw-flag").ShouldNotBeNull();
    }

    [Fact]
    public void Chosen_countries_keep_their_flag_on_the_chips()
    {
        _selection.Toggle(Countries, Countries.Options[0]);

        var chips = Render<FacetChips>(p => p.Add(c => c.Selection, _selection));

        chips.Find("[data-chip=\"Country eq 'DE'\"] img.cw-flag").GetAttribute("src").ShouldEndWith("/de.svg");
        _selection.All.Single().Display.ShouldBe(FacetDisplayAttribute.Country);
    }

    [Fact]
    public void Apps_render_their_own_displays_and_replace_the_built_in_one()
    {
        var options = Services.GetRequiredService<CoworkeeClientOptions>();
        options.ValueRenderers.Add(new PortRenderer());
        options.ValueRenderers.Add("country", c => b => b.AddContent(0, $"[{c.Value}]"));
        var ports = Countries with { Display = "port", Options = [Option("DEHAM", "Hamburg", 2)] };

        Render<FacetCheckboxView>(p => p.Add(v => v.Group, ports).Add(v => v.Selection, _selection))
            .Find("[data-option] .port").TextContent.ShouldBe("DEHAM Hamburg");
        Render<FacetCheckboxView>(p => p.Add(v => v.Group, Countries).Add(v => v.Selection, _selection))
            .Find("[data-option=\"Country eq 'DE'\"]").TextContent.ShouldContain("[DE]");
        options.ValueRenderers.Find("nothing").ShouldBeNull();
    }

    [Fact]
    public void Cells_of_country_properties_show_the_flag_and_the_text()
    {
        Services.AddSingleton<IODataClient>(new FakeODataClient());
        AddAuthorization().SetAuthorized("Ada");
        var source = NSubstitute.Substitute.For<ITableDataSource<Place>>();
        NSubstitute.SubstituteExtensions.ReturnsForAnyArgs(source.QueryAsync(default!, default),
            Task.FromResult(new ODataPage<Place>([new Place(Guid.Empty, "Berlin", "Germany")], 1, [])));
        Render<MudPopoverProvider>();

        var table = Render<CoworkeeDataTable<Place>>(p => p.Add(t => t.EntitySet, "Places").Add(t => t.DataSource, source).Add(t => t.Columns, Columns()));

        table.WaitForAssertion(() => table.Find("td img.cw-flag").GetAttribute("src").ShouldEndWith("/de.svg"));
        table.Find("td img.cw-flag").ParentElement!.TextContent.ShouldContain("Germany");
        table.FindAll("img.cw-flag").Count.ShouldBe(1, "only the marked property gets a flag");
    }

    private static RenderFragment Columns() => builder =>
    {
        builder.OpenComponent<PropertyColumn<Place, string>>(0);
        builder.AddComponentParameter(1, nameof(PropertyColumn<Place, string>.Property), (System.Linq.Expressions.Expression<Func<Place, string>>)(p => p.Name));
        builder.CloseComponent();
        builder.OpenComponent<PropertyColumn<Place, string>>(2);
        builder.AddComponentParameter(3, nameof(PropertyColumn<Place, string>.Property), (System.Linq.Expressions.Expression<Func<Place, string>>)(p => p.Country));
        builder.CloseComponent();
    };

    public sealed record Place(Guid Id, string Name, [property: CountryCode] string Country);

    private sealed class PortRenderer : IFacetValueRenderer
    {
        public string Display => "port";

        public RenderFragment Render(FacetValueContext context) => builder =>
        {
            builder.OpenElement(0, "span");
            builder.AddAttribute(1, "class", "port");
            builder.AddContent(2, $"{context.Value} {context.Label}");
            builder.CloseElement();
        };
    }
}
