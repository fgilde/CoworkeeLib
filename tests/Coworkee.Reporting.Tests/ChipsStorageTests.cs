using System.Net.Http.Json;
using Coworkee.Contracts.Reporting;

namespace Coworkee.Reporting.Tests;

public sealed class ChipsStorageTests(ReportingApp app) : ReportingTestBase(app)
{
    [Fact]
    public async Task The_order_of_the_chips_survives_storing_and_reading_the_report()
    {
        var definition = new ReportDefinition { Widgets =
        [
            new ReportWidget { Id = "chips", Type = WidgetType.Chart, Query = new WidgetQuery { EntitySet = "Sales", GroupBy = new ReportGroupBy("Region") },
                Options = new WidgetOptions { Chart = ChartKind.Chips, ValueIcons = [new("South", "<svg a/>"), new("East", "<svg b/>"), new("North", "<svg c/>"), new("West", "<svg d/>")] } },
        ] };
        var created = await Admin.PostAsJsonAsync("/api/v1/reports", new SaveReportRequest("Chips", null, null, ReportVisibility.Organisation, [], definition), Ct);
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<ReportDto>(Ct))!.Id;

        var read = await Admin.GetFromJsonAsync<ReportDto>($"/api/v1/reports/{id}", Ct);

        read!.Definition.Widgets.Single().Options.ValueIcons.Select(v => v.Value).ShouldBe(["South", "East", "North", "West"]);
    }
}
