using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts.Reporting;

namespace Coworkee.Reporting.Tests;

public sealed class ViewSwitchTests(ReportingApp app) : ReportingTestBase(app)
{
    private static SaveReportRequest Request(ReportWidget widget) => new("Switches", null, null, ReportVisibility.Private, [], new ReportDefinition { Widgets = [widget] });

    private static ReportWidget Chart(List<MetricView>? metrics = null, List<BucketView>? buckets = null) => new()
    {
        Id = "chart",
        Type = WidgetType.Chart,
        Query = new WidgetQuery { EntitySet = "Sales", GroupBy = new ReportGroupBy("Region"), DateField = "SoldAt" },
        Options = new WidgetOptions { Metrics = metrics ?? [], Buckets = buckets ?? [] },
    };

    [Fact]
    public async Task Alternatives_that_work_are_saved_with_the_report()
    {
        var widget = Chart(
            [new MetricView("Revenue", [new(AggregateFunction.Sum, "Amount")], NumberFormat.Currency), new MetricView("Open orders", [new()], Filter: "Status eq Coworkee.Reporting.Tests.SaleStatus'Open'")],
            [new BucketView("By status", "Status"), new BucketView("By month", "SoldAt", DateBucket.Month)]);

        var response = await Admin.PostAsJsonAsync("/api/v1/reports", Request(widget), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var saved = (await response.Content.ReadFromJsonAsync<ReportDto>(Ct))!.Definition.Widgets.Single().Options;
        saved.Metrics.Select(m => m.Label).ShouldBe(["Revenue", "Open orders"]);
        saved.Buckets[1].Bucket.ShouldBe(DateBucket.Month);
    }

    [Theory]
    [InlineData("metric-field", "Widgets[chart].Metrics[0].Aggregates[0]")]
    [InlineData("metric-filter", "Widgets[chart].Metrics[0].Filter")]
    [InlineData("bucket-field", "Widgets[chart].Buckets[0].GroupBy")]
    [InlineData("bucket-date", "Widgets[chart].Buckets[0].Columns")]
    [InlineData("label", "Label")]
    public async Task An_alternative_that_does_not_work_keeps_the_report_from_being_saved(string broken, string key)
    {
        var widget = broken switch
        {
            "metric-field" => Chart([new MetricView("Secret", [new(AggregateFunction.Sum, "Secret")])]),
            "metric-filter" => Chart([new MetricView("Bad", [new()], Filter: "Nope eq 1")]),
            "bucket-field" => Chart(buckets: [new BucketView("Secret", "Secret")]),
            "bucket-date" => Chart(buckets: [new BucketView("Nope", DateField: "Nope")]),
            _ => Chart([new MetricView("", [new()])]),
        };

        var response = await Admin.PostAsJsonAsync("/api/v1/reports", Request(widget), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain(key);
    }

    [Fact]
    public void A_choice_replaces_the_values_and_grouping_and_adds_its_filter()
    {
        var widget = Chart([new MetricView("Open", [new(AggregateFunction.Sum, "Amount")], NumberFormat.Currency, "Status eq 'Open'")], [new BucketView("Monthly", "SoldAt", DateBucket.Month)]);
        widget.Query!.Filter = "Region ne 'North'";

        var shown = ReportViews.Apply(widget, new ReportViewChoice(0, 0));

        shown.Query!.Aggregates.ShouldHaveSingleItem().Field.ShouldBe("Amount");
        shown.Query.Filter.ShouldBe("(Region ne 'North') and (Status eq 'Open')");
        shown.Query.GroupBy.ShouldBe(new ReportGroupBy("SoldAt", DateBucket.Month));
        shown.Options.Format.ShouldBe(NumberFormat.Currency);
        widget.Query.Aggregates.ShouldHaveSingleItem().Function.ShouldBe(AggregateFunction.Count);
        ReportViews.Apply(widget, new ReportViewChoice(5)).ShouldBeSameAs(widget);
    }

    [Fact]
    public void Choosing_a_date_field_moves_the_range_and_the_date_grouping()
    {
        var widget = Chart();
        widget.Query!.GroupBy = new ReportGroupBy("Eta", DateBucket.Month);
        widget.Query.SplitBy = new ReportGroupBy("Region");
        widget.Query.DateField = "Eta";

        var shown = ReportViews.WithDateField(widget, "Eta", "Etd");

        shown.Query!.DateField.ShouldBe("Etd");
        shown.Query.GroupBy.ShouldBe(new ReportGroupBy("Etd", DateBucket.Month));
        shown.Query.SplitBy.ShouldBe(new ReportGroupBy("Region"));
        widget.Query.DateField.ShouldBe("Eta");
        ReportViews.WithDateField(widget, "Eta", "eta").ShouldBeSameAs(widget);
        widget.Query.DateField = "Created";
        widget.Query.GroupBy = new ReportGroupBy("Region");
        ReportViews.WithDateField(widget, "Eta", "Etd").ShouldBeSameAs(widget);
    }

    [Fact]
    public async Task A_definition_naming_unknown_date_or_facet_fields_is_refused()
    {
        var request = Request(Chart());
        request.Definition.DateFields = ["SoldAt", "Region"];

        var dates = await Admin.PostAsJsonAsync("/api/v1/reports", request, Ct);

        dates.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await dates.Content.ReadAsStringAsync(Ct)).ShouldContain("Definition.DateFields");

        request.Definition.DateFields = [];
        request.Definition.FacetFields = ["Region", "Nope"];
        var facets = await Admin.PostAsJsonAsync("/api/v1/reports", request, Ct);

        facets.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await facets.Content.ReadAsStringAsync(Ct)).ShouldContain("Definition.FacetFields");

        request.Definition.DateFields = ["SoldAt", "ShippedOn"];
        request.Definition.FacetFields = ["Region", "Status"];
        var saved = await Admin.PostAsJsonAsync("/api/v1/reports", request, Ct);
        saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await saved.Content.ReadFromJsonAsync<ReportDto>(Ct))!.Definition.DateFields.ShouldBe(["SoldAt", "ShippedOn"]);
    }

    private static ReportWidget OnCosts() => new()
    {
        Id = "cost",
        Type = WidgetType.Chart,
        Query = new WidgetQuery { EntitySet = "Costs", GroupBy = new ReportGroupBy("Code"), Aggregates = [new(AggregateFunction.Sum, "Amount")] },
        Options = new WidgetOptions
        {
            Metrics = [new MetricView("Cost", [new(AggregateFunction.Sum, "Amount")], EntitySet: "Costs"), new MetricView("Count", [new()], EntitySet: "SaleRows"), new MetricView("Plain", [new()])],
        },
    };

    [Fact]
    public void A_metric_view_can_switch_the_source()
    {
        var widget = Chart([new MetricView("Count", [new()]), new MetricView("Cost", [new(AggregateFunction.Sum, "TotalCost")], EntitySet: "Costs")]);

        var shown = ReportViews.Apply(widget, new ReportViewChoice(Metric: 1));

        shown.Query!.EntitySet.ShouldBe("Costs");
        shown.Query.Aggregates.ShouldHaveSingleItem().Field.ShouldBe("TotalCost");
        widget.Query!.EntitySet.ShouldBe("Sales");
        ReportViews.Apply(widget, new ReportViewChoice(Metric: 0)).Query!.EntitySet.ShouldBe("Sales");
    }

    [Fact]
    public void A_bucket_view_can_set_or_clear_the_split()
    {
        var widget = Chart(buckets: [new BucketView("Year", "SoldAt", DateBucket.Year, SplitBy: new ReportGroupBy("TransportMode")), new BucketView("Month", "SoldAt", DateBucket.Month, NoSplit: true), new BucketView("Keep", "SoldAt", DateBucket.Day)]);
        widget.Query!.SplitBy = new ReportGroupBy("Status");

        ReportViews.Apply(widget, new ReportViewChoice(Bucket: 0)).Query!.SplitBy.ShouldBe(new ReportGroupBy("TransportMode"));
        ReportViews.Apply(widget, new ReportViewChoice(Bucket: 1)).Query!.SplitBy.ShouldBeNull();
        ReportViews.Apply(widget, new ReportViewChoice(Bucket: 2)).Query!.SplitBy.ShouldBe(new ReportGroupBy("Status"));
    }

    [Fact]
    public void A_widget_on_an_unreadable_source_shows_its_first_readable_metric()
    {
        var widget = OnCosts();

        ReportViews.ForReader(widget, _ => true).ShouldBeSameAs(widget);
        var shown = ReportViews.ForReader(widget, set => set != "Costs")!;

        shown.Query!.EntitySet.ShouldBe("SaleRows");
        shown.Options.Metrics.Count.ShouldBe(3);
        widget.Query!.EntitySet.ShouldBe("Costs");
    }

    [Fact]
    public void A_widget_without_a_readable_metric_is_left_out()
    {
        var widget = OnCosts();
        widget.Options.Metrics = [new MetricView("Cost", [new()], EntitySet: "Costs")];

        ReportViews.ForReader(widget, set => set != "Costs").ShouldBeNull();
        widget.Options.Metrics = [];
        ReportViews.ForReader(widget, set => set != "Costs").ShouldBeNull();
    }

    [Fact]
    public async Task The_check_runs_every_alternative_including_other_sources()
    {
        var widget = Chart([new MetricView("Cost", [new(AggregateFunction.Sum, "Quantity")], EntitySet: "Costs")]);
        widget.Query!.GroupBy = null;

        var response = await Admin.PostAsJsonAsync("/api/v1/reports", Request(widget), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("Widgets[chart].Metrics[0]");
    }
}
