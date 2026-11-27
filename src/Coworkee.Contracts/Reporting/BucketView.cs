namespace Coworkee.Contracts.Reporting;

/// <summary>A grouping the viewer may switch a widget to: another field and/or date bucket, or another date field for the report's date range.</summary>
/// <param name="SplitBy">Splits into series by this field instead of the widget's split.</param>
/// <param name="NoSplit">Shows no series at all (wins over <paramref name="SplitBy"/>); false keeps the widget's split.</param>
/// <param name="Field">Groups by this field; null keeps the widget's field and only changes the bucket.</param>
public sealed record BucketView(string Label, string? Field = null, DateBucket Bucket = DateBucket.None, string? DateField = null, ReportGroupBy? SplitBy = null, bool NoSplit = false);
