using System.Text.Json;
using Bunit;
using Coworkee.Client.Blazor.Reporting.Components;
using Coworkee.Contracts.Data;
using Coworkee.Contracts.Reporting;
using NSubstitute;

namespace Coworkee.Client.Blazor.Reporting.Tests;

public sealed class FilterBarTests : ReportingTestBase
{
    private static readonly ReportWidget Chart = new() { Id = "c", Type = WidgetType.Chart, Query = new WidgetQuery { EntitySet = "SalesOrders", DateField = "OrderDate" } };

    [Fact]
    public void Facets_of_the_reports_sets_come_from_OData_and_narrow_every_widget_of_the_set()
    {
        var region = new FacetGroupDto("region", "Region", "Region", FacetKind.CheckboxList, FacetOperator.Or,
            [new FacetOptionDto(JsonSerializer.SerializeToElement("North"), "North", true, 7, "Region eq 'North'")], true, 0);
        OData.QueryAsync<JsonElement>("SalesOrders", Arg.Is<Data.ODataQuery>(q => q.Facets), Arg.Any<CancellationToken>()).Returns(new ODataPage<JsonElement>([], 0, [region]));
        var popovers = Render<MudBlazor.MudPopoverProvider>();
        var state = State(Chart);
        var bar = Render<ReportFilterBar>(p => p.Add(x => x.State, state));

        bar.WaitForElement("[data-facet='region'] [data-testid='facet-button']").Click();
        popovers.WaitForElement("[data-option=\"Region eq 'North'\"]").Click();

        state.FilterFor(Chart.Query!, "other").ShouldBe("Region eq 'North'");
        bar.WaitForElement("[data-testid='facet-chips']");
    }

    [Fact]
    public void The_date_range_applies_to_widgets_with_a_date_field()
    {
        var popovers = Render<MudBlazor.MudPopoverProvider>();
        var state = State(Chart);
        var bar = Render<ReportFilterBar>(p => p.Add(x => x.State, state));

        bar.Find("[data-testid='date-range'] button").Click();
        popovers.WaitForElement("[data-preset='Last7Days']").Click();

        state.DateRange!.Preset.ShouldBe(DateRangePreset.Last7Days);
        state.FilterFor(Chart.Query!)!.ShouldStartWith("OrderDate ge ");
        bar.WaitForAssertion(() => bar.Find("[data-testid='date-range'] button").TextContent.ShouldContain("Last 7 days"));
    }

    [Fact]
    public async Task Rules_of_the_query_builder_become_the_widgets_filter_and_come_back_from_their_json()
    {
        var query = new WidgetQuery { EntitySet = "SalesOrders" };
        var editor = Render<WidgetFilterEditor>(p => p.Add(x => x.Query, query).Add(x => x.State, State(Chart)));
        var builder = editor.FindComponent<Blazor.Components.Query.QueryBuilder>();
        var rules = new Blazor.Components.Query.QueryGroup
        {
            Children = [new Blazor.Components.Query.QueryRule { Field = "Total", Operator = Blazor.Components.Query.QueryOperator.GreaterThan, Value = "100" }],
        };

        await editor.InvokeAsync(() => builder.Instance.ValueChanged.InvokeAsync(rules));

        query.Filter.ShouldBe("Total gt 100");
        Blazor.Components.Query.QueryJson.Deserialize(query.Rules)!.Rules().Single().Field.ShouldBe("Total");
        builder.Instance.Fields.Single(f => f.Name == "Status").Options!.Select(o => o.Value).ShouldBe(["Open", "Shipped"]);
    }
}
