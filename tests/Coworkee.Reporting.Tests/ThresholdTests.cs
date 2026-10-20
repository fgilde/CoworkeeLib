using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts.Reporting;

namespace Coworkee.Reporting.Tests;

public sealed class ThresholdTests(ReportingApp app) : ReportingTestBase(app)
{
    private static SaveReportRequest Request(WidgetOptions options, WidgetType type = WidgetType.Kpi) => new("Bands", null, null, ReportVisibility.Private, [], new ReportDefinition
    {
        Widgets = [new ReportWidget { Id = "w", Type = type, Query = new WidgetQuery { EntitySet = "Sales", Columns = type == WidgetType.Table ? ["Region", "Amount"] : [] }, Options = options }],
    });

    [Fact]
    public async Task Bands_with_theme_and_own_colours_are_saved()
    {
        var options = new WidgetOptions { Thresholds = [new(0, ReportThresholds.Error, "Low"), new(100, "#00aa00", "Good")], ThresholdColumn = "Amount" };

        var response = await Admin.PostAsJsonAsync("/api/v1/reports", Request(options, WidgetType.Table), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<ReportDto>(Ct))!.Definition.Widgets[0].Options.Thresholds[1].Color.ShouldBe("#00aa00");
    }

    [Theory]
    [InlineData("red;background:url(x)", null, "Color")]
    [InlineData("javascript:alert(1)", null, "Color")]
    [InlineData(ReportThresholds.Success, "Nope", "Columns")]
    public async Task Colours_that_are_no_colour_and_unknown_columns_are_refused(string color, string? column, string key)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/reports", Request(new WidgetOptions { Thresholds = [new(1, color)], ThresholdColumn = column }, WidgetType.Table), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain(key);
    }

    [Fact]
    public void A_value_takes_the_band_of_the_highest_stop_it_reaches()
    {
        List<ThresholdStop> stops = [new(100, ReportThresholds.Success), new(0, ReportThresholds.Error), new(80, ReportThresholds.Warning)];

        ReportThresholds.Find(stops, -1).ShouldBeNull();
        ReportThresholds.Find(stops, 0)!.Color.ShouldBe(ReportThresholds.Error);
        ReportThresholds.Find(stops, 99.9)!.Color.ShouldBe(ReportThresholds.Warning);
        ReportThresholds.Find(stops, 1e9)!.Color.ShouldBe(ReportThresholds.Success);
        ReportThresholds.Find(stops, null).ShouldBeNull();
    }

    [Fact]
    public void Bands_from_a_target_follow_higher_or_lower_is_better()
    {
        ReportThresholds.FromTarget(100, higherIsBetter: true).Select(s => (s.Value, s.Color)).ShouldBe([(0d, "error"), (80d, "warning"), (100d, "success")]);
        ReportThresholds.FromTarget(100, higherIsBetter: false).Select(s => (s.Value, s.Color)).ShouldBe([(0d, "success"), (100d, "warning"), (120d, "error")]);
    }
}
