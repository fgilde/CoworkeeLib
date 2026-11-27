namespace Coworkee.Contracts.Reporting;

/// <summary>A value the viewer may switch a widget to instead of its own: other aggregates, their number format and an extra filter (and-ed to the widget's).</summary>
/// <param name="EntitySet">Shows the value of this data set instead of the widget's (e.g. costs next to counts); null stays on the widget's. Viewers who may not read the set do not get the choice.</param>
public sealed record MetricView(string Label, List<ReportAggregate> Aggregates, NumberFormat? Format = null, string? Filter = null, string? EntitySet = null);
