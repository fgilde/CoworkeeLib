using Coworkee.Client.Blazor.Reporting.Widgets;
using Coworkee.Contracts.Reporting;

namespace Coworkee.Client.Blazor.Reporting.Tests;

public sealed class ChartDataTests
{
    [Fact]
    public void Duplicate_categories_of_a_series_add_up()
    {
        static ReportKey Key(string value) => new(value, value, string.Empty);
        var result = new ReportQueryResult(
        [
            new(Key("Q1"), Key("North"), [1]), new(Key("Q1"), Key("North"), [2]), new(Key("Q1"), Key("South"), [5]),
        ], 0);

        var series = ChartData.From(result, ["Count"], k => k!.Label, k => k!.Label, fillGaps: false);

        series.Select(s => (s.Name, s.Points.Single().Value)).ShouldBe([("North", 3m), ("South", 5m)]);
    }
}
