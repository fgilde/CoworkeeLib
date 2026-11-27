using System.Text.Json;
using Coworkee.Contracts.Reporting;
using Coworkee.Reporting.Querying;
using Coworkee.Reporting.Subscriptions;

namespace Coworkee.Reporting.Tests;

public sealed class SharedFacetTests
{
    private static readonly IReadOnlyList<ReportFieldDto> OrderFields = [new("TransportMode", "Mode", ReportFieldKind.Text), new("Weight", "Weight", ReportFieldKind.Number)];
    private static readonly IReadOnlyList<ReportFieldDto> CostFields = [new("TransportMode", "Mode", ReportFieldKind.Text), new("Amount", "Amount", ReportFieldKind.Number)];
    private static readonly IReadOnlyList<ReportFieldDto> OtherKind = [new("TransportMode", "Mode", ReportFieldKind.Integer)];
    private static readonly ReportFilterValue Air = new("Orders", "mode", "Mode", "Air", "TransportMode eq 'Air'", Field: "TransportMode");

    private static IReadOnlyList<ReportFieldDto>? Fields(string set, IReadOnlyList<ReportFieldDto> costs) => set switch { "Orders" => OrderFields, "Costs" => costs, _ => null };

    private static string? Filter(IReadOnlyList<ReportFieldDto> costs, params ReportFilterValue[] values) =>
        ReportFilters.For(new WidgetQuery { EntitySet = "Costs" }, new ReportDefinition { Filters = [.. values] }, costs, new DateOnly(2026, 1, 1), set => Fields(set, costs));

    [Fact]
    public void A_report_facet_applies_to_widgets_of_every_set_with_the_same_field() => Filter(CostFields, Air).ShouldBe("TransportMode eq 'Air'");

    [Fact]
    public void A_set_without_the_field_ignores_the_facet() => Filter([new("Amount", "Amount", ReportFieldKind.Number)], Air).ShouldBeNull();

    [Fact]
    public void A_field_of_another_kind_is_not_shared() => Filter(OtherKind, Air).ShouldBeNull();

    [Fact]
    public void Stored_values_without_field_use_the_group_key_as_path()
    {
        var stored = JsonSerializer.Deserialize<ReportFilterValue>("""{"EntitySet":"Orders","GroupKey":"TransportMode","GroupLabel":"Mode","Label":"Air","OData":"TransportMode eq 'Air'","Operator":0}""")!;

        stored.Field.ShouldBeNull();
        Filter(CostFields, stored).ShouldBe("TransportMode eq 'Air'");
        JsonSerializer.Serialize(Air).ShouldNotContain("\"Path\"");
    }

    [Fact]
    public void Subscription_mails_apply_shared_facets()
    {
        var property = typeof(string).GetProperty(nameof(string.Length))!;
        ReportField Field(string path, ReportFieldKind kind) => new(path, path, kind, [property], null, null);
        ReportSource Source(string set, params ReportField[] fields) => new(set, typeof(object), null, set, fields);
        var sources = new[] { Source("Orders", Field("TransportMode", ReportFieldKind.Text)), Source("Costs", Field("TransportMode", ReportFieldKind.Text), Field("Amount", ReportFieldKind.Number)) };

        ReportMailWidget.FilterOf(new WidgetQuery { EntitySet = "Costs" }, new ReportDefinition { Filters = [Air] }, sources, new DateOnly(2026, 1, 1)).ShouldBe("TransportMode eq 'Air'");
    }
}
