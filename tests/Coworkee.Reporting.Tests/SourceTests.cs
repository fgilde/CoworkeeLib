using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts.Reporting;

namespace Coworkee.Reporting.Tests;

/// <summary>Sources the app computes (AddReportSource) aggregate like entity sets.</summary>
public sealed class SourceTests(ReportingApp app) : ReportingTestBase(app)
{
    [Fact]
    public async Task Sources_list_app_sources_last_marked_computed_with_fields_from_the_row_type()
    {
        var sources = (await Admin.GetFromJsonAsync<List<ReportSourceDto>>("/api/v1/reports/sources", Ct))!;

        sources.TakeLast(2).Select(s => s.EntitySet).ShouldBe(["MonthlyNumbers", "SaleRows"]);
        var numbers = sources.Single(s => s.EntitySet == "MonthlyNumbers");
        numbers.Computed.ShouldBeTrue();
        numbers.Label.ShouldBe("Monthly numbers");
        sources.Single(s => s.EntitySet == "Sales").Computed.ShouldBeFalse();
        numbers.Fields.Select(f => (f.Path, f.Kind)).ShouldBe(
            [("Month", ReportFieldKind.Date), ("Team", ReportFieldKind.Text), ("Mood", ReportFieldKind.Enum), ("Headcount", ReportFieldKind.Integer), ("Rate", ReportFieldKind.Number)],
            ignoreOrder: true);
        numbers.Fields.Single(f => f.Path == "Headcount").Label.ShouldBe("Head count");
        numbers.Fields.Single(f => f.Path == "Month").DateOnly.ShouldBeTrue();
        numbers.Fields.Single(f => f.Path == "Mood").MemberLabels.ShouldBe(["Calm", "Very busy"]);
    }

    [Theory]
    [InlineData("MonthlyNumbers", "Team")]
    [InlineData("Sales", "Region")]
    public async Task A_filter_beyond_the_node_limit_is_refused(string source, string field)
    {
        var filter = string.Join(" or ", Enumerable.Range(0, 40).Select(i => $"contains({field},'t{i}')"));

        var response = await Admin.PostAsJsonAsync("/api/v1/reports/query", new ReportQueryRequest(source, filter, Aggregates: [new()]), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("node count");
    }

    [Fact]
    public async Task A_kpi_sums_the_filtered_rows()
    {
        var result = await QueryAsync(new ReportQueryRequest("MonthlyNumbers", "Team eq 'North' and Mood eq Coworkee.Reporting.Tests.Mood'Busy'",
            Aggregates: [new(AggregateFunction.Sum, "Headcount"), new(AggregateFunction.Average, "Rate"), new()]));

        var expected = MonthlyNumbers.All.Where(r => r.Team == "North" && r.Mood == Mood.Busy).ToList();
        var row = result.Rows.ShouldHaveSingleItem();
        row.Values[0].ShouldBe(expected.Sum(r => r.Headcount));
        row.Values[1]!.Value.ShouldBe((double)expected.Average(r => r.Rate), 0.0001);
        row.Values[2].ShouldBe(expected.Count);
    }

    [Fact]
    public async Task A_chart_groups_by_month_and_team_within_the_date_range_the_source_is_told()
    {
        var result = await QueryAsync(new ReportQueryRequest("MonthlyNumbers", "Month ge 2025-03-01 and Month lt 2025-06-01",
            new ReportGroupBy("Month", DateBucket.Month), new ReportGroupBy("Team"), [new(AggregateFunction.Sum, "Headcount")]));

        MonthlyNumbers.Last!.DateFrom.ShouldBe(new DateOnly(2025, 3, 1));
        MonthlyNumbers.Last.DateTo.ShouldBe(new DateOnly(2025, 6, 1));
        result.Rows.Select(r => (r.Key!.Value, r.Key.Label, r.Series!.Value, (int?)r.Values[0])).ShouldBe(
        [
            ("2025-03-01", "2025-03", "North", 12), ("2025-03-01", "2025-03", "South", 18),
            ("2025-04-01", "2025-04", "North", 13), ("2025-04-01", "2025-04", "South", 17),
            ("2025-05-01", "2025-05", "North", 14), ("2025-05-01", "2025-05", "South", 16),
        ]);
        result.Rows[0].Key!.Filter.ShouldBe("Month ge 2025-03-01 and Month lt 2025-04-01");
    }

    [Fact]
    public async Task Several_rows_per_category_and_series_fold_into_one_group()
    {
        var result = await QueryAsync(new ReportQueryRequest("MonthlyNumbers", GroupBy: new ReportGroupBy("Month", DateBucket.Quarter), SplitBy: new ReportGroupBy("Team"),
            Aggregates: [new(AggregateFunction.Sum, "Headcount")]));

        result.Rows.Count.ShouldBe(8);
        result.Rows.Take(2).Select(r => (r.Key!.Label, r.Series!.Value, (int?)r.Values[0])).ShouldBe([("2025 Q1", "North", 33), ("2025 Q1", "South", 57)]);
    }

    [Fact]
    public async Task Quarters_by_value_keep_the_top()
    {
        var result = await QueryAsync(new ReportQueryRequest("MonthlyNumbers", GroupBy: new ReportGroupBy("Month", DateBucket.Quarter),
            Aggregates: [new(AggregateFunction.Max, "Headcount")], SortBy: ReportSortBy.Value, Descending: true, Top: 2));

        MonthlyNumbers.Last!.DateFrom.ShouldBeNull();
        result.Rows.Select(r => (r.Key!.Label, (int?)r.Values[0])).ShouldBe([("2025 Q4", 21), ("2025 Q1", 20)]);
    }

    [Fact]
    public async Task A_source_over_the_database_keeps_the_row_filters_and_aggregates_there()
    {
        var result = await QueryAsync(new ReportQueryRequest("SaleRows", "Amount gt 500", new ReportGroupBy("Region"), Aggregates: [new()]));

        result.Rows.ToDictionary(r => r.Key!.Value!, r => (int)r.Values[0]!)
            .ShouldBe(Visible.Where(s => s.Amount > 500).GroupBy(s => s.Region).ToDictionary(g => g.Key, g => g.Count()), ignoreOrder: true);
    }

    [Fact]
    public async Task Rows_come_in_pages_with_the_chosen_fields()
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/reports/rows",
            new ReportRowsRequest("MonthlyNumbers", ["Month", "Mood", "Headcount"], "Team eq 'North'", "Headcount", Descending: true, Skip: 1, Top: 2), Ct);
        response.EnsureSuccessStatusCode();
        var page = (await response.Content.ReadFromJsonAsync<ReportRowsResult>(Ct))!;

        page.Count.ShouldBe(12);
        page.Rows.Select(r => (r.GetProperty("Month").GetString(), r.GetProperty("Mood").GetString(), r.GetProperty("Headcount").GetInt32()))
            .ShouldBe([("2025-11-01", "Calm", 20), ("2025-10-01", "Busy", 19)]);
        (await Admin.PostAsJsonAsync("/api/v1/reports/rows", new ReportRowsRequest("MonthlyNumbers", ["Nope"]), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.PostAsJsonAsync("/api/v1/reports/rows", new ReportRowsRequest("Sales", ["Region"]), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_permission_of_the_source_applies()
    {
        var viewer = App.As((await CreateUserAsync("numbers-viewer@acme.test", ReportPermissions.View)).Id, Setup.TenantId);
        var reader = App.As((await CreateUserAsync("numbers-reader@acme.test", ReportPermissions.View, MonthlyNumbers.ViewNumbers)).Id, Setup.TenantId);

        (await viewer.PostAsJsonAsync("/api/v1/reports/query", new ReportQueryRequest("MonthlyNumbers"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await viewer.PostAsJsonAsync("/api/v1/reports/rows", new ReportRowsRequest("MonthlyNumbers", ["Team"]), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await viewer.GetFromJsonAsync<List<ReportSourceDto>>("/api/v1/reports/sources", Ct))!.ShouldNotContain(s => s.Computed);
        (await QueryAsync(new ReportQueryRequest("MonthlyNumbers"), reader)).Rows.Single().Values[0].ShouldBe(MonthlyNumbers.All.Count);
    }

    [Theory]
    [InlineData("Nope eq 1", "Filter")]
    [InlineData("Team eq", "Filter")]
    public async Task Bad_filters_are_rejected(string filter, string field)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/reports/query", new ReportQueryRequest("MonthlyNumbers", filter), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain($"\"{field}\"");
    }

    [Fact]
    public async Task A_report_with_a_kpi_and_a_chart_on_a_source_saves()
    {
        var definition = new ReportDefinition
        {
            Widgets =
            [
                new ReportWidget { Id = "kpi", Type = WidgetType.Kpi, Query = new WidgetQuery { EntitySet = "MonthlyNumbers", DateField = "Month", Aggregates = [new(AggregateFunction.Sum, "Headcount")] } },
                new ReportWidget
                {
                    Id = "chart", Type = WidgetType.Chart, W = 6,
                    Query = new WidgetQuery { EntitySet = "MonthlyNumbers", Filter = "Team eq 'North'", GroupBy = new ReportGroupBy("Month", DateBucket.Month), Aggregates = [new(AggregateFunction.Sum, "Headcount")] },
                },
            ],
        };

        var response = await Admin.PostAsJsonAsync("/api/v1/reports", new SaveReportRequest("Numbers", null, null, ReportVisibility.Private, [], definition), Ct);

        response.EnsureSuccessStatusCode();
        var bad = new SaveReportRequest("Bad", null, null, ReportVisibility.Private, [],
            new ReportDefinition { Widgets = [new ReportWidget { Type = WidgetType.Kpi, Query = new WidgetQuery { EntitySet = "MonthlyNumbers", Filter = "Nope eq 1" } }] });
        (await Admin.PostAsJsonAsync("/api/v1/reports", bad, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
