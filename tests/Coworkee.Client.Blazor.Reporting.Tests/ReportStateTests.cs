using Coworkee.Client.Blazor.Reporting.State;
using Coworkee.Contracts.Data;
using Coworkee.Contracts.Reporting;
using Microsoft.Extensions.Time.Testing;

namespace Coworkee.Client.Blazor.Reporting.Tests;

public sealed class ReportStateTests
{
    private static readonly ReportSourceDto Orders = new("SalesOrders", "Sales orders",
        [new ReportFieldDto("OrderDate", "Order date", ReportFieldKind.Date), new ReportFieldDto("ShippedOn", "Shipped on", ReportFieldKind.Date, DateOnly: true)]);

    private static readonly FakeTimeProvider Clock = new(new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void A_widget_gets_its_own_filter_the_report_facets_the_date_range_and_the_cross_filters_of_others()
    {
        var definition = new ReportDefinition
        {
            Filters = [new ReportFilterValue("SalesOrders", "region", "Region", "North", "Region eq 'North'"), new ReportFilterValue("Products", "brand", "Brand", "Acme", "Brand eq 'Acme'")],
            DateRange = new ReportDateRange(DateRangePreset.ThisMonth),
        };
        var state = new ReportState(definition, [Orders], Clock);
        var query = new WidgetQuery
        {
            EntitySet = "SalesOrders", Filter = "Total gt 10", DateField = "OrderDate",
            Facets = [new ReportFilterValue("SalesOrders", "status", "Status", "Open", "Status eq 'Open'")],
        };
        state.ToggleCross(new CrossFilter("chart", "SalesOrders", "Channel: Web", "Channel eq 'Web'"));
        state.ToggleCross(new CrossFilter("other", "Products", "Brand", "Brand eq 'X'"));

        state.FilterFor(query, "table").ShouldBe(
            "(Total gt 10) and (Status eq 'Open') and (Region eq 'North') and (OrderDate ge 2026-10-01T00:00:00Z and OrderDate lt 2026-11-01T00:00:00Z) and (Channel eq 'Web')");
        state.FilterFor(query, "chart")!.ShouldNotContain("Channel");
        state.FilterFor(query, "table", previousPeriod: true)!.ShouldContain("OrderDate ge 2026-09-01T00:00:00Z and OrderDate lt 2026-10-01T00:00:00Z");
    }

    [Fact]
    public void Clicking_the_same_category_again_removes_the_cross_filter()
    {
        var state = new ReportState(new ReportDefinition(), [Orders]);
        var filter = new CrossFilter("chart", "SalesOrders", "North", "Region eq 'North'");

        state.ToggleCross(filter);
        state.ToggleCross(filter with { Filter = "Region eq 'South'" });
        state.CrossFilters.ShouldHaveSingleItem().Filter.ShouldBe("Region eq 'South'");
        state.ToggleCross(filter with { Filter = "Region eq 'South'" });

        state.CrossFilters.ShouldBeEmpty();
    }

    [Fact]
    public void Facets_picked_in_the_report_are_kept_in_its_definition()
    {
        var definition = new ReportDefinition();
        var state = new ReportState(definition, [Orders]);

        state.Facets("SalesOrders").Toggle(new FacetGroupDto("region", "Region", "Region", FacetKind.CheckboxList, FacetOperator.Or, [], true, 0),
            new FacetOptionDto(default, "North", true, 3, "Region eq 'North'"));

        definition.Filters.ShouldHaveSingleItem().ShouldBe(new ReportFilterValue("SalesOrders", "region", "Region", "North", "Region eq 'North'"));
    }

    [Theory]
    [InlineData(DateRangePreset.Last7Days, "2026-10-05", "2026-10-12")]
    [InlineData(DateRangePreset.LastMonth, "2026-09-01", "2026-10-01")]
    [InlineData(DateRangePreset.ThisQuarter, "2026-10-01", "2027-01-01")]
    [InlineData(DateRangePreset.Last12Months, "2025-11-01", "2026-11-01")]
    public void Presets_resolve_to_days(DateRangePreset preset, string from, string to)
    {
        DateRanges.Resolve(new ReportDateRange(preset), new DateOnly(2026, 10, 11)).ShouldBe((DateOnly.Parse(from), DateOnly.Parse(to)));
    }

    [Fact]
    public void The_previous_period_has_the_same_length_and_whole_months_stay_months()
    {
        DateRanges.Previous((new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 1))).ShouldBe((new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1)));
        DateRanges.Previous((new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 12))).ShouldBe((new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 5)));
        DateRanges.Filter("ShippedOn", (new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1)), dateOnly: true).ShouldBe("ShippedOn ge 2026-01-01 and ShippedOn lt 2026-02-01");
    }

    [Theory]
    [InlineData("Last12Months", "Last 12 months")]
    [InlineData("InProgress", "In progress")]
    [InlineData("Open", "Open")]
    public void Names_read_as_words(string name, string text) => ReportFormat.Humanize(name).ShouldBe(text);

    [Fact]
    public void Numbers_follow_the_format_and_scripts_only_take_currency_codes()
    {
        var culture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        ReportFormat.Number(27_900_000, new WidgetOptions { Format = NumberFormat.Compact }, culture).ShouldBe("27.9M");
        ReportFormat.Number(12.5, new WidgetOptions { Format = NumberFormat.Percent }, culture).ShouldBe("12.5%");
        ReportFormat.Number(null, new WidgetOptions(), culture).ShouldBe("–");
        ReportFormat.Script(new WidgetOptions { Format = NumberFormat.Currency, Currency = "x');alert(1);('" }, culture).ShouldContain("currency:'EUR'");
    }
}
