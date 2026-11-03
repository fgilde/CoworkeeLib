namespace Coworkee.Contracts.Reporting;

/// <summary>Whether reports can be built with AI: a model is configured and the user may create reports.</summary>
public sealed record ReportAiStatusDto(bool Available);
