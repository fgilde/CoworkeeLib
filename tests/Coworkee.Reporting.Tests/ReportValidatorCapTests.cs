using Coworkee.Contracts.Reporting;

namespace Coworkee.Reporting.Tests;

// a stored definition is read by every viewer: lists and texts that grow without bound are refused on save
public sealed class ReportValidatorCapTests
{
    private static SaveReportRequest Request(Action<ReportDefinition> change)
    {
        var definition = new ReportDefinition
        {
            Widgets =
            [
                new ReportWidget { Id = "w", Type = WidgetType.Chart, Query = new WidgetQuery { EntitySet = "Sales", GroupBy = new ReportGroupBy("Region") } },
            ],
        };
        change(definition);
        return new SaveReportRequest("Caps", null, null, ReportVisibility.Private, [], definition);
    }

    private static ReportWidget Widget(ReportDefinition definition) => definition.Widgets[0];

    public static TheoryData<string, Action<ReportDefinition>> TooMuch => new()
    {
        { "date fields", d => d.DateFields = [.. Enumerable.Range(0, 9).Select(i => $"Date{i}")] },
        { "filter bar fields", d => d.FacetFields = [.. Enumerable.Range(0, 21).Select(i => $"Field{i}")] },
        { "value icons", d => Widget(d).Options.ValueIcons = [.. Enumerable.Range(0, 51).Select(i => new ValueIcon($"V{i}", "<svg/>"))] },
        { "icon length", d => Widget(d).Options.ValueIcons = [new ValueIcon("Air", new string('x', 4001))] },
        { "icon value length", d => Widget(d).Options.ValueIcons = [new ValueIcon(new string('v', 201), "<svg/>")] },
        { "unit", d => Widget(d).Query!.Aggregates = [new ReportAggregate(Unit: new string('k', 17))] },
        { "label", d => Widget(d).Query!.Aggregates = [new ReportAggregate(Label: new string('l', 101))] },
        { "metric unit", d => Widget(d).Options.Metrics = [new MetricView("M", [new ReportAggregate(Unit: new string('k', 17))])] },
        { "join label", d => Widget(d).Query!.Joins = [new WidgetJoin { EntitySet = "Sales", Aggregates = [new ReportAggregate(Label: new string('l', 101))] }] },
    };

    [Theory]
    [MemberData(nameof(TooMuch))]
    public void Saving_refuses_definitions_beyond_the_caps(string what, Action<ReportDefinition> change)
    {
        new SaveReportValidator().Validate(Request(change)).IsValid.ShouldBeFalse(what);
    }

    [Fact]
    public void Saving_takes_definitions_at_the_caps()
    {
        var request = Request(d =>
        {
            d.DateFields = [.. Enumerable.Range(0, 8).Select(i => $"Date{i}")];
            d.FacetFields = [.. Enumerable.Range(0, 20).Select(i => $"Field{i}")];
            Widget(d).Options.ValueIcons = [.. Enumerable.Range(0, 50).Select(i => new ValueIcon($"V{i}", new string('x', 4000)))];
            Widget(d).Query!.Aggregates = [new ReportAggregate(Label: new string('l', 100), Unit: new string('k', 16))];
        });

        var result = new SaveReportValidator().Validate(request);

        result.IsValid.ShouldBeTrue(string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
    }
}
