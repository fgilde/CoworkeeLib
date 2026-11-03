using Coworkee.Contracts.Reporting;

namespace Coworkee.Reporting.Querying;

/// <param name="WeekIndex">SQL Server has no day of week independent of its settings: weeks are counted from a Monday instead.</param>
/// <param name="BinaryDate">A DateTimeOffset of a Coworkee DbContext on SQLite, stored as a number (DateTimeOffsetToBinaryConverter): its parts are computed from it.</param>
internal sealed record ReportGroup(ReportField Field, DateBucket Bucket, bool WeekIndex = false, bool BinaryDate = false);
