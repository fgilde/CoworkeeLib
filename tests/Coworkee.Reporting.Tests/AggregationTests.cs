using Coworkee.Contracts.Reporting;

namespace Coworkee.Reporting.Tests;

/// <summary>The database's groups and values match the same aggregation done with LINQ over the seeded rows.</summary>
public sealed class AggregationTests(ReportingApp app) : ReportingTestBase(app)
{
    private static readonly ReportAggregate[] AllFunctions =
    [
        new(AggregateFunction.Count),
        new(AggregateFunction.Sum, "Amount"),
        new(AggregateFunction.Average, "Amount"),
        new(AggregateFunction.Min, "Quantity"),
        new(AggregateFunction.Max, "Quantity"),
        new(AggregateFunction.DistinctCount, "ShopperId"),
    ];

    [Fact]
    public async Task Every_function_per_region_matches_linq()
    {
        var result = await QueryAsync(new ReportQueryRequest("Sales", GroupBy: new ReportGroupBy("Region"), Aggregates: AllFunctions));

        var expected = Visible.GroupBy(s => s.Region).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
        result.Rows.Select(r => r.Key!.Value).ShouldBe(expected.Select(g => g.Key));
        foreach (var (row, group) in result.Rows.Zip(expected))
        {
            row.Values[0].ShouldBe(group.Count());
            row.Values[1]!.Value.ShouldBe((double)group.Sum(s => s.Amount), 0.01);
            row.Values[2]!.Value.ShouldBe((double)group.Average(s => s.Amount), 0.01);
            row.Values[3].ShouldBe(group.Min(s => s.Quantity));
            row.Values[4].ShouldBe(group.Max(s => s.Quantity));
            row.Values[5].ShouldBe(group.Select(s => s.ShopperId).Distinct().Count());
        }
    }

    [Fact]
    public async Task Totals_without_group_cover_only_visible_rows_of_the_tenant()
    {
        var result = await QueryAsync(new ReportQueryRequest("Sales", Aggregates: [new(AggregateFunction.Count), new(AggregateFunction.Sum, "Amount")]));

        var row = result.Rows.ShouldHaveSingleItem();
        row.Key.ShouldBeNull();
        row.Values[0].ShouldBe(Visible.Count());
        row.Values[1]!.Value.ShouldBe((double)Visible.Sum(s => s.Amount), 0.01);
        Visible.Count().ShouldBeLessThan(Sales.Count);
    }

    [Theory]
    [InlineData(DateBucket.Year)]
    [InlineData(DateBucket.Quarter)]
    [InlineData(DateBucket.Month)]
    [InlineData(DateBucket.Week)]
    [InlineData(DateBucket.Day)]
    public async Task Date_buckets_match_linq(DateBucket bucket)
    {
        var result = await QueryAsync(new ReportQueryRequest("Sales", GroupBy: new ReportGroupBy("SoldAt", bucket), Aggregates: [new(), new(AggregateFunction.Sum, "Amount")]));

        var expected = Visible.GroupBy(s => Start(s.SoldAt.UtcDateTime, bucket)).OrderBy(g => g.Key).ToList();
        result.Rows.Select(r => r.Key!.Value).ShouldBe(expected.Select(g => g.Key.ToString("yyyy-MM-dd")));
        result.Rows.Select(r => r.Values[0]).ShouldBe(expected.Select(g => (double?)g.Count()));
        result.Rows.Zip(expected).ShouldAllBe(p => Math.Abs(p.First.Values[1]!.Value - (double)p.Second.Sum(s => s.Amount)) < 0.01);
    }

    [Fact]
    public async Task Nullable_date_only_fields_bucket_with_an_empty_group()
    {
        var result = await QueryAsync(new ReportQueryRequest("Sales", GroupBy: new ReportGroupBy("ShippedOn", DateBucket.Month)));

        var expected = Visible.GroupBy(s => s.ShippedOn is { } day ? new DateOnly(day.Year, day.Month, 1).ToString("yyyy-MM-dd") : null).ToDictionary(g => g.Key ?? "-", g => g.Count());
        result.Rows.ToDictionary(r => r.Key!.Value ?? "-", r => (int)r.Values[0]!).ShouldBe(expected, ignoreOrder: true);
        result.Rows.Last().Key!.Value.ShouldBeNull();
    }

    [Fact]
    public async Task Series_split_categories_and_keep_the_top_by_value()
    {
        var result = await QueryAsync(new ReportQueryRequest("Sales", GroupBy: new ReportGroupBy("Region"), SplitBy: new ReportGroupBy("Status"),
            Aggregates: [new(AggregateFunction.Sum, "Amount")], SortBy: ReportSortBy.Value, Descending: true, Top: 2));

        var top = Visible.GroupBy(s => s.Region).OrderByDescending(g => g.Sum(s => s.Amount)).Take(2).Select(g => g.Key).ToList();
        result.Rows.Select(r => r.Key!.Value).Distinct().ShouldBe(top);
        foreach (var row in result.Rows)
        {
            var expected = Visible.Where(s => s.Region == row.Key!.Value && s.Status.ToString() == row.Series!.Value).Sum(s => s.Amount);
            row.Values[0]!.Value.ShouldBe((double)expected, 0.01);
        }
    }

    [Fact]
    public async Task Top_by_value_sorts_in_the_database()
    {
        var result = await QueryAsync(new ReportQueryRequest("Sales", GroupBy: new ReportGroupBy("Shopper/Name"), Aggregates: [new(AggregateFunction.Sum, "Quantity")],
            SortBy: ReportSortBy.Value, Descending: true, Top: 3));

        var expected = Visible.GroupBy(s => s.Shopper!.Name).Select(g => (g.Key, Sum: g.Sum(s => s.Quantity))).OrderByDescending(g => g.Sum).Take(3).ToList();
        result.Rows.Select(r => (r.Key!.Value, (int)r.Values[0]!)).ShouldBe(expected.Select(e => ((string?)e.Key, e.Sum)));
    }

    [Fact]
    public async Task Key_filters_select_the_rows_of_their_group()
    {
        var byStatus = await QueryAsync(new ReportQueryRequest("Sales", "Amount gt 250", new ReportGroupBy("Status")));
        var byMonth = await QueryAsync(new ReportQueryRequest("Sales", GroupBy: new ReportGroupBy("SoldAt", DateBucket.Month)));

        foreach (var row in byStatus.Rows.Concat(byMonth.Rows.Take(3)))
        {
            var filter = byStatus.Rows.Contains(row) ? $"Amount gt 250 and {row.Key!.Filter}" : row.Key!.Filter;
            (await QueryAsync(new ReportQueryRequest("Sales", filter))).Rows.Single().Values[0].ShouldBe(row.Values[0]);
        }

        byStatus.Rows.Select(r => r.Key!.Label).ShouldBe(["Open", "Shipped", "Cancelled"]);
        byStatus.Rows.Sum(r => r.Values[0]).ShouldBe(Visible.Count(s => s.Amount > 250));
    }

    private static DateTime Start(DateTime date, DateBucket bucket) => bucket switch
    {
        DateBucket.Year => new DateTime(date.Year, 1, 1),
        DateBucket.Quarter => new DateTime(date.Year, ((date.Month - 1) / 3 * 3) + 1, 1),
        DateBucket.Month => new DateTime(date.Year, date.Month, 1),
        DateBucket.Week => date.Date.AddDays(-(((int)date.DayOfWeek + 6) % 7)),
        _ => date.Date,
    };
}
