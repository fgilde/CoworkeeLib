namespace Coworkee.Contracts.Reporting;

public sealed record ReportDateRange(DateRangePreset Preset, DateOnly? From = null, DateOnly? To = null);
