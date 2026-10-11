using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts.Reporting;

namespace Coworkee.Reporting.Tests;

public sealed class QueryAccessTests(ReportingApp app) : ReportingTestBase(app)
{
    [Theory]
    [InlineData("{\"entitySet\":\"Sales\",\"groupBy\":{\"field\":\"Nope\"}}", "GroupBy")]
    [InlineData("{\"entitySet\":\"Sales\",\"groupBy\":{\"field\":\"Secret\"}}", "GroupBy")]
    [InlineData("{\"entitySet\":\"Sales\",\"groupBy\":{\"field\":\"Region\",\"bucket\":\"Month\"}}", "GroupBy")]
    [InlineData("{\"entitySet\":\"Sales\",\"aggregates\":[{\"function\":\"Sum\",\"field\":\"Region\"}]}", "Aggregates[0]")]
    [InlineData("{\"entitySet\":\"Sales\",\"aggregates\":[{\"function\":\"Average\"}]}", "Aggregates[0]")]
    [InlineData("{\"entitySet\":\"Sales\",\"splitBy\":{\"field\":\"Status\"}}", "SplitBy")]
    [InlineData("{\"entitySet\":\"Sales\",\"top\":5000}", "Top")]
    [InlineData("{\"entitySet\":\"Sales\",\"filter\":\"Secret eq 's'\"}", "Filter")]
    [InlineData("{\"entitySet\":\"Sales\",\"filter\":\"Region eq 'x') or (1 eq 1\"}", "Filter")]
    [InlineData("{\"entitySet\":\"Sales\",\"groupBy\":{\"field\":\"Region\"},\"aggregates\":[{\"function\":\"Unknown\"}]}", null)]
    public async Task Bad_fields_functions_and_filters_are_rejected(string body, string? field)
    {
        var response = await Admin.PostAsync("/api/v1/reports/query", new StringContent(body, System.Text.Encoding.UTF8, "application/json"), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        if (field is not null)
        {
            (await response.Content.ReadAsStringAsync(Ct)).ShouldContain($"\"{field}\"");
        }
    }

    [Fact]
    public async Task Unknown_sets_are_not_found()
    {
        (await Admin.PostAsJsonAsync("/api/v1/reports/query", new ReportQueryRequest("Users2"), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_permission_of_the_set_and_of_navigations_applies()
    {
        var viewer = await CreateUserAsync("viewer@acme.test", ReportPermissions.View);
        var seller = await CreateUserAsync("seller@acme.test", ReportPermissions.View, ReportingApp.ViewSales);
        var outsider = await CreateUserAsync("outsider@acme.test", ReportingApp.ViewSales);
        var asViewer = App.As(viewer.Id, Setup.TenantId);
        var asSeller = App.As(seller.Id, Setup.TenantId);

        (await asViewer.PostAsJsonAsync("/api/v1/reports/query", new ReportQueryRequest("Sales"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await App.As(outsider.Id, Setup.TenantId).PostAsJsonAsync("/api/v1/reports/query", new ReportQueryRequest("Sales"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await QueryAsync(new ReportQueryRequest("Sales"), asSeller)).Rows.Single().Values[0].ShouldBe(Visible.Count());
        (await asSeller.PostAsJsonAsync("/api/v1/reports/query", new ReportQueryRequest("Sales", GroupBy: new ReportGroupBy("Shopper/Name")), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var sources = await asSeller.GetFromJsonAsync<List<ReportSourceDto>>("/api/v1/reports/sources", Ct);
        var sales = sources.ShouldHaveSingleItem();
        sales.Fields.Select(f => f.Path).ShouldBe(["Region", "Status", "Amount", "Quantity", "SoldAt", "ShippedOn", "ShopperId", "Id"], ignoreOrder: true);
        sales.Fields.Single(f => f.Path == "Status").Members.ShouldBe(["Open", "Shipped", "Cancelled"]);

        var all = await Admin.GetFromJsonAsync<List<ReportSourceDto>>("/api/v1/reports/sources", Ct);
        all!.Single(s => s.EntitySet == "Sales").Fields.Single(f => f.Path == "Shopper/Name").Label.ShouldBe("Shopper name");
    }

    [Fact]
    public async Task Other_tenants_see_none_of_the_rows()
    {
        var response = await App.As(Setup.AdminUserId, Guid.NewGuid()).PostAsJsonAsync("/api/v1/reports/query", new ReportQueryRequest("Sales"), Ct);

        response.StatusCode.ShouldBeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.OK);
        if (response.IsSuccessStatusCode)
        {
            (await response.Content.ReadFromJsonAsync<ReportQueryResult>(Ct))!.Rows.Single().Values[0].ShouldBe(0);
        }
    }

    [Fact]
    public async Task A_sheet_comes_back_as_workbook()
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/reports/sheet",
            new ReportSheetRequest("Sales: by region", ["Region", "Count"], [[System.Text.Json.JsonSerializer.SerializeToElement("North"), System.Text.Json.JsonSerializer.SerializeToElement(3)]]), Ct);

        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        (await response.Content.ReadAsByteArrayAsync(Ct)).Length.ShouldBeGreaterThan(1000);
    }
}
