namespace Coworkee.Contracts.Reporting;

public sealed record ReportAggregate(AggregateFunction Function = AggregateFunction.Count, string? Field = null, string? Label = null);
