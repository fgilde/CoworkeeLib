using Coworkee.Contracts.Reporting;

namespace Coworkee.Reporting.Querying;

internal sealed record ReportValue(AggregateFunction Function, ReportField? Field, bool EmptyAsZero = false);
