namespace Coworkee.Contracts.Reporting;

/// <summary>A value the viewer may switch a widget to instead of its own: other aggregates, their number format and an extra filter (and-ed to the widget's).</summary>
public sealed record MetricView(string Label, List<ReportAggregate> Aggregates, NumberFormat? Format = null, string? Filter = null);
