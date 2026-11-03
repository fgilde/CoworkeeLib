namespace Coworkee.Contracts.Reporting;

/// <summary>The viewer's choice of a widget: an index into <see cref="WidgetOptions.Metrics"/> and <see cref="WidgetOptions.Buckets"/>, null for the widget's own.</summary>
public sealed record ReportViewChoice(int? Metric = null, int? Bucket = null)
{
    public bool IsDefault => Metric is null && Bucket is null;
}
