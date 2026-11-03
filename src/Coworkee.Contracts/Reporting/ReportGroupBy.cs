namespace Coworkee.Contracts.Reporting;

public sealed record ReportGroupBy(string Field, DateBucket Bucket = DateBucket.None);
