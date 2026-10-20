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
}
