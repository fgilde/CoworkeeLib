namespace Coworkee.Contracts.Reporting;

/// <summary>A report to build from <see cref="Prompt"/> with the app's AI model, or <see cref="Current"/> to change; the answer is not saved.</summary>
public sealed record GenerateReportRequest(string Prompt, ReportDefinition? Current = null);
