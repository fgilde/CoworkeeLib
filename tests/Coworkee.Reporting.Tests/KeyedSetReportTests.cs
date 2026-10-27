using System.Net.Http.Json;
using Coworkee.Contracts.Reporting;

namespace Coworkee.Reporting.Tests;

/// <summary>Reports on a set keyed by a code instead of a Guid Id.</summary>
public sealed class KeyedSetReportTests(ReportingApp app) : ReportingTestBase(app)
{
    [Fact]
    public async Task A_set_keyed_by_a_code_groups_filters_and_aggregates()
    {
        await App.InDbAsync(Setup.TenantId, async db =>
        {
            db.AddRange(
                new Store { Code = "N1", Region = "North", Seats = 10, TenantId = Setup.TenantId },
                new Store { Code = "N2", Region = "North", Seats = 5, TenantId = Setup.TenantId },
                new Store { Code = "S1", Region = "South", Seats = 7, TenantId = Setup.TenantId });
            await db.SaveChangesAsync();
        });

        var rows = (await QueryAsync(new ReportQueryRequest("Stores", GroupBy: new ReportGroupBy("Region"), Aggregates: [new(AggregateFunction.Sum, "Seats")]))).Rows;
        rows.Select(r => (r.Key!.Value, r.Values[0])).ShouldBe([("North", 15d), ("South", 7d)]);

        var byCode = (await QueryAsync(new ReportQueryRequest("Stores", Filter: "Code ne 'N2'", GroupBy: new ReportGroupBy("Code")))).Rows;
        byCode.Select(r => r.Key!.Value).ShouldBe(["N1", "S1"]);
    }

    [Fact]
    public async Task Sqlite_refuses_date_buckets_of_a_DateTimeOffset_with_its_own_conversion()
    {
        Assert.SkipUnless(App.Provider == Coworkee.Contracts.Configuration.DatabaseProviders.Sqlite, "Only SQLite computes date parts from the stored number.");

        var response = await Admin.PostAsJsonAsync("/api/v1/reports/query", new ReportQueryRequest("Stores", GroupBy: new ReportGroupBy("OpenedAt", DateBucket.Month)), Ct);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("SQLite cannot group OpenedAt");
        (await QueryAsync(new ReportQueryRequest("Sales", GroupBy: new ReportGroupBy("SoldAt", DateBucket.Month)))).Rows.ShouldNotBeEmpty();
    }
}
