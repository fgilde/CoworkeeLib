namespace Coworkee.Contracts.Reporting;

/// <summary>A grouping the viewer may switch a widget to: another field and/or date bucket, or another date field for the report's date range.</summary>
/// <param name="Field">Groups by this field; null keeps the widget's field and only changes the bucket.</param>
public sealed record BucketView(string Label, string? Field = null, DateBucket Bucket = DateBucket.None, string? DateField = null);
