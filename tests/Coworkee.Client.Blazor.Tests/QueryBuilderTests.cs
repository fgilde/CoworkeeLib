using Bunit;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Components.Query;
using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Coworkee.Client.Blazor.Tests;

public sealed class QueryBuilderTests : ClientTestBase
{
    private static readonly QueryField[] Fields =
    [
        new("Name", "Name", QueryFieldType.Text),
        new("Price", "Price", QueryFieldType.Number),
        new("Kind", "Kind", QueryFieldType.Enum) { Options = [new("Hand", "Hand tool"), new("Power", "Power tool")] },
    ];

    [Fact]
    public async Task Rules_and_groups_are_added_edited_moved_and_removed()
    {
        var popovers = Render<MudPopoverProvider>();
        var query = new QueryGroup();
        var changes = 0;
        var builder = Render<QueryBuilder>(p => p.Add(b => b.Fields, Fields).Add(b => b.Value, query).Add(b => b.ValueChanged, _ => changes++));

        await builder.Find("[data-testid='query-add-rule']").ClickAsync(new());
        await builder.Find("[data-testid='query-value'] input, input[data-testid='query-value']").InputAsync(new ChangeEventArgs { Value = "dri" });
        builder.WaitForAssertion(() => ODataQueryTranslator.ToFilter(query, Fields).ShouldBe("contains(tolower(Name),'dri')"));

        await builder.Find("[data-testid='query-add-group']").ClickAsync(new());
        var nested = (QueryGroup)query.Children[1];
        nested.Logic.ShouldBe(QueryLogic.Or);
        ((QueryRule)nested.Children[0]).Operator = QueryOperator.LessThan;
        ((QueryRule)nested.Children[0]).Field = "Price";
        ((QueryRule)nested.Children[0]).Value = "5";
        await builder.Find("[data-depth='0'] input[data-testid='query-not'], [data-depth='0'] [data-testid='query-not'] input").ChangeAsync(new ChangeEventArgs { Value = true });
        ODataQueryTranslator.ToFilter(query, Fields).ShouldBe("not (contains(tolower(Name),'dri') and Price lt 5)");

        await builder.FindAll("[data-testid='query-move-down']")[0].ClickAsync(new());
        query.Children[0].ShouldBeOfType<QueryGroup>();
        await builder.FindAll("[data-testid='query-remove-group']")[0].ClickAsync(new());
        query.Children.Count.ShouldBe(1);
        await builder.Find("[data-testid='query-remove-rule']").ClickAsync(new());
        query.IsEmpty.ShouldBeTrue();
        changes.ShouldBeGreaterThan(5);
        popovers.Markup.ShouldNotBeNull();
    }

    [Fact]
    public void The_field_and_operator_pickers_reset_values_that_no_longer_fit()
    {
        var popovers = Render<MudPopoverProvider>();
        var query = new QueryGroup { Children = [new QueryRule { Field = "Name", Operator = QueryOperator.Contains, Value = "x" }] };
        var builder = Render<QueryBuilder>(p => p.Add(b => b.Fields, Fields).Add(b => b.Value, query));

        builder.Find("[data-field='Name'] .mud-input-control").MouseDown();
        popovers.WaitForElement(".mud-list-item:nth-of-type(3), .mud-list-item").ShouldNotBeNull();
        popovers.FindAll(".mud-list-item").Single(i => i.TextContent.Contains("Kind")).Click();

        builder.WaitForAssertion(() => ((QueryRule)query.Children[0]).Field.ShouldBe("Kind"));
        var rule = (QueryRule)query.Children[0];
        (rule.Operator, rule.Value).ShouldBe((QueryOperator.In, null));
        builder.Find("[data-testid='query-values']").ShouldNotBeNull();
    }

    [Fact]
    public async Task The_preview_reads_as_text_odata_and_json_and_json_imports()
    {
        var query = new QueryGroup { Children = [new QueryRule { Field = "Kind", Operator = QueryOperator.In, Values = ["Hand", "Power"] }] };
        var builder = Render<QueryBuilder>(p => p.Add(b => b.Fields, Fields).Add(b => b.Value, query));

        builder.Find("[data-testid='query-preview-text']").TextContent.ShouldBe("Kind is any of Hand tool, Power tool");
        await builder.FindAll("[data-testid='query-preview-mode'] .mud-toggle-item")[1].ClickAsync(new());
        builder.Find("[data-testid='query-preview-text']").TextContent.ShouldBe("(Kind eq 'Hand' or Kind eq 'Power')");
        builder.Find("[data-testid='query-export']").GetAttribute("href")!.ShouldStartWith("data:application/json");

        await builder.Find("[data-testid='query-import']").ClickAsync(new());
        await builder.Find("textarea[data-testid='query-import-text'], [data-testid='query-import-text'] textarea").ChangeAsync(new ChangeEventArgs { Value = """{"logic":"Or","children":[{"kind":"rule","field":"Price","operator":"GreaterThan","value":"10"}]}""" });
        await builder.Find("[data-testid='query-import-apply']").ClickAsync(new());

        query.Logic.ShouldBe(QueryLogic.Or);
        ODataQueryTranslator.ToFilter(query, Fields).ShouldBe("Price gt 10");
        await builder.Find("[data-testid='query-import']").ClickAsync(new());
        await builder.Find("textarea[data-testid='query-import-text'], [data-testid='query-import-text'] textarea").ChangeAsync(new ChangeEventArgs { Value = "nope" });
        await builder.Find("[data-testid='query-import-apply']").ClickAsync(new());
        builder.Markup.ShouldContain("This is not a query.");
    }

    [Fact]
    public void The_table_filters_with_the_advanced_filter_and_keeps_it_in_its_state()
    {
        var odata = new FakeODataClient().With("Gadgets", new DataTableTests.Gadget(Guid.NewGuid(), "Drill", "Tools"));
        ODataQuery? last = null;
        odata.Queried += (_, q) => last = q.Facets ? q : last;
        Services.AddSingleton<IODataClient>(odata);
        AddAuthorization().SetAuthorized("Ada");
        Render<MudPopoverProvider>();
        var table = Render<CoworkeeDataTable<DataTableTests.Gadget>>(p => p.Add(t => t.EntitySet, "Gadgets").Add(t => t.Columns, (RenderFragment)(b =>
        {
            b.OpenComponent<PropertyColumn<DataTableTests.Gadget, string>>(0);
            b.AddComponentParameter(1, "Property", (System.Linq.Expressions.Expression<Func<DataTableTests.Gadget, string>>)(g => g.Name));
            b.CloseComponent();
        })));
        table.WaitForAssertion(() => last.ShouldNotBeNull());

        table.Find("[data-testid='advanced-filter']").Click();
        table.Find("[data-testid='query-add-rule']").Click();
        table.Find("[data-testid='advanced-panel'] [data-testid='query-value'] input, [data-testid='advanced-panel'] input[data-testid='query-value']").Input("dri");

        table.WaitForAssertion(() => last!.Filter.ShouldBe("contains(tolower(Name),'dri')"));
        table.WaitForAssertion(() => table.Find("[data-filter-chip='advanced']").TextContent.ShouldContain("Name contains \"dri\""));
        var state = table.Instance.CurrentState;
        DataTableState.Decode(state.Encode())!.Advanced.ShouldNotBeNull();
        ODataQueryTranslator.ToFilter(DataTableState.Decode(state.Encode())!.Advanced!, [new("Name", "Name", QueryFieldType.Text)]).ShouldBe("contains(tolower(Name),'dri')");

        table.Find("[data-filter-chip='advanced'] .mud-chip-close-button").Click();
        table.WaitForAssertion(() => last!.Filter.ShouldBeNull());
    }

    [Fact]
    public void The_table_sends_the_facet_choice_apart_and_shows_enum_labels()
    {
        var status = new FacetGroupDto("status", "Status", "Status", FacetKind.CheckboxList, FacetOperator.Or,
            [new(System.Text.Json.JsonSerializer.SerializeToElement("Shipped"), "On the way", true, 2, "Status eq 'Shipped'")], true, 0) { ValueKind = FacetValueKind.Enum };
        var odata = new FakeODataClient().With("Gadgets", new DataTableTests.Gadget(Guid.NewGuid(), "Drill", "Tools")).WithFacets(status);
        ODataQuery? last = null;
        odata.Queried += (_, q) => last = q.Facets ? q : last;
        Services.AddSingleton<IODataClient>(odata);
        AddAuthorization().SetAuthorized("Ada");
        var popovers = Render<MudPopoverProvider>();
        var table = Render<CoworkeeDataTable<DataTableTests.Gadget>>(p => p.Add(t => t.EntitySet, "Gadgets").Add(t => t.SearchFields, ["Name"]));
        table.WaitForAssertion(() => table.Find("[data-facet='status']"));

        table.Find("[data-facet='status'] [data-testid='facet-button']").Click();
        popovers.WaitForElement("[data-option=\"Status eq 'Shipped'\"]").TextContent.ShouldContain("On the way");
        popovers.Find("[data-option=\"Status eq 'Shipped'\"]").Click();

        table.WaitForAssertion(() => last!.FacetContext!.Groups["status"].ShouldBe("Status eq 'Shipped'"));
        last!.FacetContext!.Base.ShouldBeNull();
        table.Find("[data-testid='facet-chips']").TextContent.ShouldContain("On the way");
    }
}
