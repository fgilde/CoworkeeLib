using System.Text.Json;
using Bunit;
using Coworkee.Client.Blazor.Reporting.Components;
using Coworkee.Client.Blazor.Reporting.State;
using Coworkee.Contracts.Data;
using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Reporting;
using Coworkee.Contracts.Settings;
using Microsoft.Extensions.DependencyInjection;
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
    public void The_filter_bar_merges_facets_by_key_across_sets()
    {
        var costs = new ReportSourceDto("Costs", "Costs", [new ReportFieldDto("Region", "Region", ReportFieldKind.Text)]);
        var region = new FacetGroupDto("region", "Region", "Region", FacetKind.CheckboxList, FacetOperator.Or,
            [new FacetOptionDto(JsonSerializer.SerializeToElement("North"), "North", true, 7, "Region eq 'North'")], true, 0);
        OData.QueryAsync<JsonElement>(Arg.Any<string>(), Arg.Is<Data.ODataQuery>(q => q.Facets), Arg.Any<CancellationToken>()).Returns(new ODataPage<JsonElement>([], 0, [region]));
        var costsWidget = new ReportWidget { Id = "k", Type = WidgetType.Chart, Query = new WidgetQuery { EntitySet = "Costs" } };
        var state = new ReportState(new ReportDefinition { Widgets = [Chart, costsWidget] }, [Orders, costs]);
        var popovers = Render<MudBlazor.MudPopoverProvider>();
        var bar = Render<ReportFilterBar>(p => p.Add(x => x.State, state));

        bar.WaitForAssertion(() => bar.FindAll("[data-facet='region']").Count.ShouldBe(1));
        bar.Find("[data-facet='region'] [data-testid='facet-button']").Click();
        popovers.WaitForElement("[data-option=\"Region eq 'North'\"]").Click();

        state.Definition.Filters.ShouldHaveSingleItem().Field.ShouldBe("Region");
        state.FilterFor(costsWidget.Query!).ShouldBe("Region eq 'North'");
    }

    [Fact]
    public void The_date_range_applies_to_widgets_with_a_date_field()
    {
        var popovers = Render<MudBlazor.MudPopoverProvider>();
        var state = State(Chart);
        var bar = Render<ReportFilterBar>(p => p.Add(x => x.State, state));

        bar.Find("[data-testid='date-range'] button").Click();
        popovers.WaitForElement("[data-preset='Last7Days']").Click();

        bar.WaitForAssertion(() => state.DateRange?.Preset.ShouldBe(DateRangePreset.Last7Days));
        state.FilterFor(Chart.Query!)!.ShouldStartWith("OrderDate ge ");
        bar.WaitForAssertion(() => bar.Find("[data-testid='date-range'] button").TextContent.ShouldContain("Last 7 days"));
    }

    [Fact]
    public async Task Rules_of_the_query_builder_become_the_widgets_filter_and_come_back_from_their_json()
    {
        var query = new WidgetQuery { EntitySet = "SalesOrders" };
        var editor = Render<WidgetFilterEditor>(p => p.Add(x => x.Query, query).Add(x => x.State, State(Chart)));
        var builder = editor.FindComponent<Blazor.Components.Query.QueryBuilder>();
        var rules = new Coworkee.Contracts.Query.QueryGroup
        {
            Children = [new Coworkee.Contracts.Query.QueryRule { Field = "Total", Operator = Coworkee.Contracts.Query.QueryOperator.GreaterThan, Value = "100" }],
        };

        await editor.InvokeAsync(() => builder.Instance.ValueChanged.InvokeAsync(rules));

        query.Filter.ShouldBe("Total gt 100");
        Coworkee.Contracts.Query.QueryJson.Deserialize(query.Rules)!.Rules().Single().Field.ShouldBe("Total");
        builder.Instance.Fields.Single(f => f.Name == "Status").Options!.Select(o => o.Value).ShouldBe(["Open", "Shipped"]);
    }

    private static readonly ReportSourceDto Shipments = new("Shipments", "Shipments",
    [
        new ReportFieldDto("Eta", "ETA", ReportFieldKind.Date),
        new ReportFieldDto("Etd", "ETD", ReportFieldKind.Date),
        new ReportFieldDto("Region", "Region", ReportFieldKind.Text),
        new ReportFieldDto("Carrier", "Carrier", ReportFieldKind.Text),
    ]);

    private static readonly ReportWidget ShipmentChart = new()
    {
        Id = "s", Type = WidgetType.Chart, Query = new WidgetQuery { EntitySet = "Shipments", DateField = "Eta", GroupBy = new ReportGroupBy("Eta", DateBucket.Month) },
    };

    private static FacetGroupDto Group(string key, string field, params string[] values) =>
        new(key, key, field, FacetKind.CheckboxList, FacetOperator.Or,
            [.. values.Select(v => new FacetOptionDto(JsonSerializer.SerializeToElement(v), v, true, 5, $"{field} eq '{v}'"))], true, 0);

    private static ReportState Shipping(ReportDefinition definition, params ReportSourceDto[] sources) => new(definition, sources.Length == 0 ? [Shipments] : sources);

    private static void Select(IRenderedComponent<ReportFilterBar> bar, string field) =>
        bar.InvokeAsync(() => bar.FindComponent<MudBlazor.MudSelect<string>>().Instance.ValueChanged.InvokeAsync(field)).GetAwaiter().GetResult();

    [Fact]
    public void A_custom_range_follows_the_chosen_date_field()
    {
        var state = Shipping(new ReportDefinition { Widgets = [ShipmentChart], DateFields = ["Eta", "Etd"], DateRange = new ReportDateRange(DateRangePreset.Custom, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31)) });
        Reports.QueryAsync(default!, default).ReturnsForAnyArgs(Rows(("2026-03-01", null, 3)));
        var bar = Render<ReportFilterBar>(p => p.Add(x => x.State, state));
        var frame = Render<WidgetFrame>(p => p.Add(x => x.Widget, ShipmentChart).Add(x => x.State, state));
        frame.WaitForAssertion(() => Reports.Received().QueryAsync(Arg.Is<ReportQueryRequest>(r => r.Filter!.StartsWith("Eta ge ") && r.GroupBy!.Field == "Eta"), Arg.Any<CancellationToken>()));

        Select(bar, "Etd");

        frame.WaitForAssertion(() => Reports.Received().QueryAsync(
            Arg.Is<ReportQueryRequest>(r => r.Filter!.StartsWith("Etd ge ") && r.Filter.Contains("Etd lt ") && r.GroupBy!.Field == "Etd" && r.GroupBy.Bucket == DateBucket.Month), Arg.Any<CancellationToken>()));
        ShipmentChart.Query!.DateField.ShouldBe("Eta");
    }

    [Fact]
    public void The_date_field_choice_is_offered_and_remembered()
    {
        var reportId = Guid.NewGuid();
        var settings = Services.GetRequiredService<ICoworkeeApi>();
        var state = Shipping(new ReportDefinition { Widgets = [ShipmentChart], DateFields = ["Eta", "Etd"] });
        var bar = Render<ReportFilterBar>(p => p.Add(x => x.State, state).Add(x => x.ReportId, reportId));
        bar.Find("[data-testid='date-field']").TextContent.ShouldContain("ETA");

        Select(bar, "Etd");

        state.DateField.ShouldBe("Etd");
        settings.Received().SetSettingsAsync(SettingScope.User,
            Arg.Is<IReadOnlyDictionary<string, string?>>(v => v[TableSettings.ReportViews]!.Contains($"{reportId:N}:date") && v[TableSettings.ReportViews]!.Contains("\"dateField\":\"Etd\"")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void A_stored_date_field_is_applied_when_the_bar_opens()
    {
        var reportId = Guid.NewGuid();
        Services.GetRequiredService<ICoworkeeApi>().GetSettingsAsync(SettingScope.User, default)
            .ReturnsForAnyArgs([new SettingValueDto(TableSettings.ReportViews, "{\"" + reportId.ToString("N") + ":date\":{\"dateField\":\"Etd\"}}", true)]);
        var state = Shipping(new ReportDefinition { Widgets = [ShipmentChart], DateFields = ["Eta", "Etd"] });

        var bar = Render<ReportFilterBar>(p => p.Add(x => x.State, state).Add(x => x.ReportId, reportId));

        bar.WaitForAssertion(() => state.DateField.ShouldBe("Etd"));
        bar.WaitForAssertion(() => bar.Find("[data-testid='date-field']").TextContent.ShouldContain("ETD"));
        // the report no longer offers the choice: the default again
        state.Load(new ReportDefinition { Widgets = [ShipmentChart], DateFields = ["Eta"] });
        state.DateField.ShouldBeNull();
    }

    [Fact]
    public void Facet_fields_limit_the_filter_bar_and_add_non_facet_fields()
    {
        OData.QueryAsync<JsonElement>("Shipments", Arg.Is<Data.ODataQuery>(q => q.Facets), Arg.Any<CancellationToken>())
            .Returns(new ODataPage<JsonElement>([], 0, [Group("mode", "Mode", "Air"), Group("region", "Region", "North")]));
        Reports.QueryAsync(default!, default).ReturnsForAnyArgs(new ReportQueryResult([new ReportQueryRow(new ReportKey("Maersk", "Maersk", "Carrier eq 'Maersk'"), null, [4])], 1));
        var state = Shipping(new ReportDefinition { Widgets = [ShipmentChart], FacetFields = ["Region", "Carrier"] });

        var bar = Render<ReportFilterBar>(p => p.Add(x => x.State, state));

        bar.WaitForAssertion(() => bar.FindAll("[data-testid='facet-bar'] [data-facet]").Select(e => e.GetAttribute("data-facet")).ShouldBe(["region", "Carrier"]));
        Reports.Received().QueryAsync(Arg.Is<ReportQueryRequest>(r => r.EntitySet == "Shipments" && r.GroupBy!.Field == "Carrier" && r.Top == 1000 && r.Aggregates!.Single().Function == AggregateFunction.Count), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void A_value_of_a_field_without_facet_is_a_filter_with_its_field_and_narrows_the_other_counts()
    {
        var popovers = Render<MudBlazor.MudPopoverProvider>();
        OData.QueryAsync<JsonElement>("Shipments", Arg.Is<Data.ODataQuery>(q => q.Facets), Arg.Any<CancellationToken>()).Returns(new ODataPage<JsonElement>([], 0, [Group("region", "Region", "North")]));
        Reports.QueryAsync(default!, default).ReturnsForAnyArgs(new ReportQueryResult([new ReportQueryRow(new ReportKey("Maersk", "Maersk", "Carrier eq 'Maersk'"), null, [4])], 1));
        var state = Shipping(new ReportDefinition { Widgets = [ShipmentChart], FacetFields = ["Region", "Carrier"] });
        var bar = Render<ReportFilterBar>(p => p.Add(x => x.State, state));
        bar.WaitForElement("[data-facet='Carrier'] [data-testid='facet-button']").Click();
        popovers.WaitForElement("[data-option=\"Carrier eq 'Maersk'\"]").Click();

        state.Definition.Filters.ShouldHaveSingleItem().Field.ShouldBe("Carrier");
        state.FilterFor(ShipmentChart.Query!).ShouldBe("Carrier eq 'Maersk'");
        // the region counts see the carrier; the carrier's own values do not
        bar.WaitForAssertion(() => OData.Received().QueryAsync<JsonElement>("Shipments",
            Arg.Is<Data.ODataQuery>(q => q.Facets && q.Filter == "Carrier eq 'Maersk'" && q.FacetContext!.Groups.ContainsKey("Carrier")), Arg.Any<CancellationToken>()));
        Reports.Received().QueryAsync(Arg.Is<ReportQueryRequest>(r => r.GroupBy!.Field == "Carrier" && r.Filter == null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Facet_counts_see_the_other_sets_shared_facets_and_the_cross_filters_but_not_their_own_choice()
    {
        var costs = new ReportSourceDto("Costs", "Costs", [new ReportFieldDto("Region", "Region", ReportFieldKind.Text), new ReportFieldDto("Amount", "Amount", ReportFieldKind.Number)]);
        var popovers = Render<MudBlazor.MudPopoverProvider>();
        OData.QueryAsync<JsonElement>(Arg.Any<string>(), Arg.Is<Data.ODataQuery>(q => q.Facets), Arg.Any<CancellationToken>()).Returns(new ODataPage<JsonElement>([], 0, [Group("Region", "Region", "North")]));
        var costsWidget = new ReportWidget { Id = "k", Type = WidgetType.Chart, Query = new WidgetQuery { EntitySet = "Costs" } };
        var state = Shipping(new ReportDefinition { Widgets = [ShipmentChart, costsWidget] }, Shipments, costs);
        var bar = Render<ReportFilterBar>(p => p.Add(x => x.State, state));
        bar.WaitForElement("[data-facet='Region'] [data-testid='facet-button']").Click();

        popovers.WaitForElement("[data-option=\"Region eq 'North'\"]").Click();

        // the set that shows the facet counts it without its own choice; the other set counts with it
        bar.WaitForAssertion(() => OData.Received().QueryAsync<JsonElement>("Shipments",
            Arg.Is<Data.ODataQuery>(q => q.Facets && q.Filter == "Region eq 'North'" && q.FacetContext!.Base == null && q.FacetContext.Groups.ContainsKey("Region")), Arg.Any<CancellationToken>()));
        bar.WaitForAssertion(() => OData.Received().QueryAsync<JsonElement>("Costs",
            Arg.Is<Data.ODataQuery>(q => q.Facets && q.Filter == "Region eq 'North'" && q.FacetContext == null), Arg.Any<CancellationToken>()));

        // a lane (cross filter) narrows the counts of every set it applies to
        state.ToggleCross(new CrossFilter("c", "Shipments", "Maersk", "Carrier eq 'Maersk'"));

        state.ContextFor("Shipments").ShouldBe("Carrier eq 'Maersk'");
        state.ContextFor("Costs").ShouldBe("Region eq 'North'");
        bar.WaitForAssertion(() => OData.Received().QueryAsync<JsonElement>("Shipments",
            Arg.Is<Data.ODataQuery>(q => q.Facets && q.FacetContext!.Base == "Carrier eq 'Maersk'"), Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void A_group_without_a_field_is_shared_by_its_key_in_the_bar_and_in_the_filter()
    {
        var costs = new ReportSourceDto("Costs", "Costs", [new ReportFieldDto("Region", "Region", ReportFieldKind.Text)]);
        var state = new ReportState(new ReportDefinition
        {
            Filters = [new ReportFilterValue("SalesOrders", "Region", "Region", "North", "Region eq 'North'")],
        }, [Orders, costs]);

        state.CanShare("Region", null, "SalesOrders", "Costs").ShouldBeTrue();
        state.CanShare("Mode", null, "SalesOrders", "Costs").ShouldBeFalse();
        state.FilterFor(new WidgetQuery { EntitySet = "Costs" }).ShouldBe("Region eq 'North'");
    }
}
