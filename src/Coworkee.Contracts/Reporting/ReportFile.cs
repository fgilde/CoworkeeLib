namespace Coworkee.Contracts.Reporting;

/// <summary>The file a report is exported to and imported from.</summary>
public sealed record ReportFile(string Name, string? Description, string? Icon, ReportDefinition Definition)
{
    public const string Extension = ".report.json";
}
