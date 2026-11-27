namespace Coworkee.Contracts.Reporting;

/// <summary>The viewer's choice of a widget: an index into <see cref="WidgetOptions.Metrics"/> and <see cref="WidgetOptions.Buckets"/>, null for the widget's own. <see cref="DateField"/> is the report-wide choice of <see cref="ReportDefinition.DateFields"/> (stored under <see cref="ReportViews.DateKey"/>).</summary>
public sealed record ReportViewChoice(int? Metric = null, int? Bucket = null, string? DateField = null)
{
    public bool IsDefault => Metric is null && Bucket is null && DateField is null;
}
