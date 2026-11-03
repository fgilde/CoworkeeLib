namespace Coworkee.Contracts.Reporting;

public sealed record ReportQueryRow(ReportKey? Key, ReportKey? Series, IReadOnlyList<double?> Values);
