using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts.DataFilters;
using Coworkee.Contracts.Query;
using Coworkee.Contracts.Reporting;
using Coworkee.OData.DataFilters;
using Coworkee.Reporting.Querying;

namespace Coworkee.Reporting.Tests;

/// <summary>Grouped tables: several group fields, values of joined sets merged by key (like ISNULL(x,'') under a case-insensitive collation), sorted and paged.</summary>
public sealed class TableQueryTests(ReportingApp app) : ReportingTestBase(app)
{
    private static readonly StringComparer Text = StringComparer.InvariantCultureIgnoreCase;

    private static readonly string[] ByLane = ["Region", "Channel"];

    [Fact]
    public async Task A_table_groups_by_several_fields_with_values()
    {
        var result = await TableAsync(new ReportTableRequest("Sales", null, ByLane,
            [new(), new(AggregateFunction.Sum, "Amount"), new(AggregateFunction.Average, "Days", EmptyAsZero: true)], Top: 100));

        var expected = Lanes(Visible).ToList();
        result.More.ShouldBeFalse();
        result.Rows.Select(r => (r.Keys[0]!.Value, r.Keys[1]!.Value)).ShouldBe(expected.Select(g => (g.Key.Region, g.Key.Channel)));
        expected.ShouldContain(g => g.Key.Channel == null);
        foreach (var (row, group) in result.Rows.Zip(expected))
        {
            row.Values[0].ShouldBe(group.Count());
            row.Values[1]!.Value.ShouldBe((double)group.Sum(s => s.Amount), 0.01);
            row.Values[2]!.Value.ShouldBe(group.Sum(s => s.Days ?? 0) / (double)group.Count(), 0.001);
        }

        var empty = result.Rows.First(r => r.Keys[1]!.Value is null);
        empty.Keys[1]!.Filter.ShouldBe("(Channel eq null or Channel eq '')");
        empty.Keys[0]!.Filter.ShouldBe($"Region eq '{empty.Keys[0]!.Value}'");
    }

    [Fact]
    public async Task Joined_sources_merge_by_the_group_fields_treating_null_as_empty_and_case_alike()
    {
        var result = await TableAsync(new ReportTableRequest("Sales", null, ByLane, [new()],
            [new ReportTableJoin("Returns", "Region ne 'WEST' and Region ne 'west'", [new(), new(AggregateFunction.Sum, "Amount")])], Top: 100));

        var expected = Lanes(Visible).ToList();
        result.Rows.Select(r => (r.Keys[0]!.Value, r.Keys[1]!.Value)).ShouldBe(expected.Select(g => (g.Key.Region, g.Key.Channel)));
        foreach (var (row, group) in result.Rows.Zip(expected))
        {
            var returned = group.Where(s => s.Quantity > 10).ToList();
            row.Values.Count.ShouldBe(3);
            row.Values[0].ShouldBe(group.Count());
            if (group.Key.Region == "West")
            {
                // the join's filter leaves the lane out: no values, not zero
                row.Values[1].ShouldBeNull();
                row.Values[2].ShouldBeNull();
                continue;
            }

            // the returns spell the region in upper and lower case and the empty channel as "" and null: all of them count for the lane
            returned.ShouldNotBeEmpty();
            row.Values[1].ShouldBe(returned.Count, $"{group.Key}");
            row.Values[2]!.Value.ShouldBe((double)returned.Sum(s => s.Amount), 0.01);
        }

        result.Rows.ShouldContain(r => r.Keys[1]!.Value == null && r.Values[1] > 0);
    }

    [Fact]
    public async Task Averages_with_empty_as_zero_and_distinct_counts_of_a_join_count_every_spelling_of_a_lane_once()
    {
        var result = await TableAsync(new ReportTableRequest("Sales", null, ByLane, [new()],
            [new ReportTableJoin("Returns", null, [new(AggregateFunction.Average, "Days", EmptyAsZero: true), new(AggregateFunction.DistinctCount, "Quantity")])], Top: 100));

        var expected = Lanes(Visible).ToList();
        result.Rows.Count.ShouldBe(expected.Count);
        // a lane's returns spell the region in upper (even quantity) and lower case (odd) and its empty channel as "" (even) and null (odd)
        expected.ShouldContain(g => g.Key.Channel == null && g.Where(s => s.Quantity > 10).Select(s => s.Quantity % 2).Distinct().Count() == 2);
        foreach (var (row, group) in result.Rows.Zip(expected))
        {
            var returned = group.Where(s => s.Quantity > 10).ToList();
            returned.ShouldNotBeEmpty();
            row.Values[1]!.Value.ShouldBe(returned.Sum(s => s.Days ?? 0) / (double)returned.Count, 0.001, $"{group.Key}");
            row.Values[2].ShouldBe(returned.Select(s => s.Quantity).Distinct().Count(), $"{group.Key}");
        }
    }

    [Fact]
    public async Task Empty_and_missing_texts_of_the_main_set_are_one_row_with_the_joined_values_once()
    {
        // the returns spell the empty channel as "" (even quantity) and null (odd)
        var result = await TableAsync(new ReportTableRequest("Returns", null, ["Channel"],
            [new(), new(AggregateFunction.Sum, "Amount"), new(AggregateFunction.Average, "Days", EmptyAsZero: true)],
            [new ReportTableJoin("Sales", null, [new()])], Top: 100));

        var returned = Visible.Where(s => s.Quantity > 10).ToList();
        returned.Where(s => s.Channel == null).Select(s => s.Quantity % 2).Distinct().Count().ShouldBe(2);
        var expected = returned.GroupBy(s => s.Channel).OrderBy(g => g.Key, Text).ToList();
        result.Rows.Select(r => r.Keys[0]!.Value).ShouldBe(expected.Select(g => g.Key));
        foreach (var (row, group) in result.Rows.Zip(expected))
        {
            row.Values[0].ShouldBe(group.Count(), $"{group.Key}");
            row.Values[1]!.Value.ShouldBe((double)group.Sum(s => s.Amount), 0.01);
            row.Values[2]!.Value.ShouldBe(group.Sum(s => s.Days ?? 0) / (double)group.Count(), 0.001);
            row.Values[3].ShouldBe(Visible.Count(s => s.Channel == group.Key), $"{group.Key}");
        }

        var empty = result.Rows[0];
        (empty.Keys[0]!.Label, empty.Keys[0]!.Filter).ShouldBe((string.Empty, "(Channel eq null or Channel eq '')"));
    }

    [Fact]
    public async Task Main_groups_alike_ignoring_case_take_a_joined_value_once()
    {
        // the returns spell the region in upper and lower case: one group on SQL Server, two on Postgres and SQLite; the sales' count once either way
        var result = await TableAsync(new ReportTableRequest("Returns", null, ["Region"], [new()], [new ReportTableJoin("Sales", null, [new()])], Top: 100));

        var returned = Visible.Where(s => s.Quantity > 10).ToList();
        var regions = result.Rows.GroupBy(r => r.Keys[0]!.Value!, Text).ToList();
        regions.Select(g => g.Key).Order(Text).ShouldBe(returned.Select(s => s.Region).Distinct().Order(Text), Text);
        foreach (var region in regions)
        {
            region.Sum(r => r.Values[0]).ShouldBe(returned.Count(s => Text.Equals(s.Region, region.Key)), region.Key);
            region.Count(r => r.Values[1] != null).ShouldBe(1, region.Key);
            region.Sum(r => r.Values[1]).ShouldBe(Visible.Count(s => Text.Equals(s.Region, region.Key)), region.Key);
        }
    }

    [Fact]
    public void Keys_equal_ignoring_case_keep_one_order_whatever_order_the_database_returns()
    {
        // Postgres groups "North" and "north" apart; sorted alike ignoring case, they need a final order for stable pages
        var region = new ReportGroup(new ReportField("Region", "Region", ReportFieldKind.Text, [typeof(ReturnRow).GetProperty(nameof(ReturnRow.Region))!], null, null), DateBucket.None);
        List<(object?[] Keys, double?[] Values)> rows = [(["north"], [1]), (["North"], [1]), (["NORTH"], [1])];

        var forward = ReportTables.Merge([region], rows, [], [new(1)], 0, 10).Rows.Select(r => r.Keys[0]!.Value).ToList();
        rows.Reverse();
        var backward = ReportTables.Merge([region], rows, [], [new(1)], 0, 10).Rows.Select(r => r.Keys[0]!.Value).ToList();

        forward.ShouldBe(["NORTH", "North", "north"]);
        backward.ShouldBe(forward);
    }

    [Fact]
    public async Task The_table_sorts_by_any_column_then_by_every_group_field_and_pages()
    {
        // by the count, largest first, ties by region and channel
        var page = await TableAsync(new ReportTableRequest("Sales", null, ByLane, [new()], OrderBy: [new(2, Descending: true)], Skip: 2, Top: 2));

        var byCount = Lanes(Visible).OrderByDescending(g => g.Count()).ThenBy(g => g.Key.Region, Text).ThenBy(g => g.Key.Channel, Text).ToList();
        page.Rows.Select(r => (r.Keys[0]!.Value, r.Keys[1]!.Value, r.Values[0])).ShouldBe(byCount.Skip(2).Take(2).Select(g => (g.Key.Region, g.Key.Channel, (double?)g.Count())));
        page.More.ShouldBeTrue();

        // by the channel descending: the empty channel last, as on SQL Server
        var all = await TableAsync(new ReportTableRequest("Sales", null, ByLane, [new()], OrderBy: [new(1, Descending: true)], Top: 100));
        var byChannel = Lanes(Visible).OrderByDescending(g => g.Key.Channel, Text).ThenBy(g => g.Key.Region, Text).ToList();
        all.Rows.Select(r => (r.Keys[0]!.Value, r.Keys[1]!.Value)).ShouldBe(byChannel.Select(g => (g.Key.Region, g.Key.Channel)));
        all.Rows[^1].Keys[1]!.Value.ShouldBeNull();
        all.More.ShouldBeFalse();

        // the last page
        var last = await TableAsync(new ReportTableRequest("Sales", null, ByLane, [new()], Skip: byChannel.Count - 1, Top: 5));
        last.Rows.Count.ShouldBe(1);
        last.More.ShouldBeFalse();
    }

    [Fact]
    public async Task A_join_on_an_unreadable_source_is_forbidden()
    {
        var seller = App.As((await CreateUserAsync("table-seller@acme.test", ReportPermissions.View, ReportingApp.ViewSales)).Id, Setup.TenantId);

        var join = await seller.PostAsJsonAsync("/api/v1/reports/table",
            new ReportTableRequest("Sales", null, ["Region"], [new()], [new ReportTableJoin("Returns", null, [new()])]), Ct);
        var main = await seller.PostAsJsonAsync("/api/v1/reports/table", new ReportTableRequest("Returns", null, ["Region"], [new()]), Ct);

        join.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        main.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await TableAsync(new ReportTableRequest("Sales", null, ["Region"], [new()]), seller)).Rows.Count.ShouldBe(4);
    }

    [Theory]
    [InlineData("{\"entitySet\":\"Sales\",\"groupFields\":[],\"aggregates\":[]}", "GroupFields")]
    [InlineData("{\"entitySet\":\"Sales\",\"groupFields\":[\"Region\",\"Nope\"],\"aggregates\":[]}", "GroupFields[1]")]
    [InlineData("{\"entitySet\":\"Sales\",\"groupFields\":[\"Region\",\"Channel\"],\"aggregates\":[],\"joins\":[{\"entitySet\":\"SaleRows\",\"aggregates\":[]}]}", "Joins[0].GroupFields[1]")]
    [InlineData("{\"entitySet\":\"Sales\",\"groupFields\":[\"Region\"],\"aggregates\":[],\"joins\":[{\"entitySet\":\"Returns\",\"aggregates\":[{\"function\":\"Sum\",\"field\":\"Region\"}]}]}", "Joins[0].Aggregates[0]")]
    [InlineData("{\"entitySet\":\"Sales\",\"groupFields\":[\"Region\"],\"aggregates\":[],\"joins\":[{\"entitySet\":\"Returns\",\"filter\":\"Nope eq 1\",\"aggregates\":[]}]}", "Joins[0].Filter")]
    [InlineData("{\"entitySet\":\"Sales\",\"groupFields\":[\"Region\"],\"aggregates\":[],\"orderBy\":[{\"column\":1}]}", "OrderBy[0]")]
    [InlineData("{\"entitySet\":\"Sales\",\"groupFields\":[\"Region\"],\"aggregates\":[],\"top\":0}", "Top")]
    [InlineData("{\"entitySet\":\"Sales\",\"groupFields\":[\"Region\"],\"aggregates\":[],\"skip\":-1}", "Skip")]
    [InlineData("{\"entitySet\":\"Sales\",\"groupFields\":[\"Region\"],\"aggregates\":[],\"joins\":[{\"entitySet\":\"Returns\",\"aggregates\":[]},{\"entitySet\":\"Returns\",\"aggregates\":[]},{\"entitySet\":\"Returns\",\"aggregates\":[]},{\"entitySet\":\"Returns\",\"aggregates\":[]}]}", "Joins")]
    public async Task Bad_tables_are_validation_errors(string body, string field)
    {
        var response = await Admin.PostAsync("/api/v1/reports/table", new StringContent(body, System.Text.Encoding.UTF8, "application/json"), Ct);

        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, text);
        text.ShouldContain($"\"{field}\"");
    }

    [Fact]
    public async Task Too_many_groups_is_a_validation_error()
    {
        ReportTables.MaxRows = 5;
        try
        {
            var groups = await Admin.PostAsJsonAsync("/api/v1/reports/table", new ReportTableRequest("Sales", null, ByLane, [new()], Top: 5), Ct);
            var top = await Admin.PostAsJsonAsync("/api/v1/reports/table", new ReportTableRequest("Sales", null, ["Status"], [new()], Top: 6), Ct);

            groups.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await groups.Content.ReadAsStringAsync(Ct)).ShouldContain("More than 5 groups");
            top.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await TableAsync(new ReportTableRequest("Sales", null, ["Status"], [new()], Top: 5))).Rows.Count.ShouldBe(3);
        }
        finally
        {
            ReportTables.MaxRows = 50_000;
        }
    }

    [Fact]
    public async Task Row_filters_and_data_filters_apply_to_every_source()
    {
        var user = await CreateUserAsync("table-territory@acme.test", ReportPermissions.View, ReportingApp.ViewSales, MonthlyNumbers.ViewNumbers);
        SouthOnlyFilter.Users[user.Id] = true;
        await App.InDbAsync(null, async db =>
        {
            db.Add(new DataFilter
            {
                TenantId = Setup.TenantId, EntitySet = "Sales", Layer = DataFilterLayer.Tenant, Version = 1, AppliesToDetails = true,
                Query = QueryJson.Serialize(new QueryGroup { Children = [new QueryRule { Field = "Amount", Operator = QueryOperator.GreaterThan, Value = "500" }] }),
            });
            await db.SaveChangesAsync(Ct);
        });

        var result = await TableAsync(new ReportTableRequest("Sales", null, ByLane, [new()],
            [new ReportTableJoin("Sales", "Status eq Coworkee.Reporting.Tests.SaleStatus'Shipped'", [new()]), new ReportTableJoin("Returns", null, [new(AggregateFunction.Sum, "Amount")])], Top: 100),
            App.As(user.Id, Setup.TenantId));

        var seen = Visible.Where(s => s.Region == "South" && s.Amount > 500).ToList();
        var expected = Lanes(seen).ToList();
        expected.Count.ShouldBeGreaterThan(1);
        result.Rows.Select(r => (r.Keys[0]!.Value, r.Keys[1]!.Value)).ShouldBe(expected.Select(g => (g.Key.Region, g.Key.Channel)));
        foreach (var (row, group) in result.Rows.Zip(expected))
        {
            row.Values[0].ShouldBe(group.Count());
            row.Values[1].ShouldBe(group.Count(s => s.Status == SaleStatus.Shipped) is var shipped and > 0 ? shipped : null);
            var returned = group.Where(s => s.Quantity > 10).ToList();
            if (returned.Count == 0)
            {
                row.Values[2].ShouldBeNull();
            }
            else
            {
                row.Values[2]!.Value.ShouldBe((double)returned.Sum(s => s.Amount), 0.01);
            }
        }
    }

    private static IEnumerable<IGrouping<(string? Region, string? Channel), Sale>> Lanes(IEnumerable<Sale> sales) =>
        sales.GroupBy(s => (Region: (string?)s.Region, s.Channel)).OrderBy(g => g.Key.Region, Text).ThenBy(g => g.Key.Channel, Text);

    private async Task<ReportTableResult> TableAsync(ReportTableRequest request, HttpClient? client = null)
    {
        var response = await (client ?? Admin).PostAsJsonAsync("/api/v1/reports/table", request, Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);
        response.IsSuccessStatusCode.ShouldBeTrue(body);
        return System.Text.Json.JsonSerializer.Deserialize<ReportTableResult>(body, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
    }
}
