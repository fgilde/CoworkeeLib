namespace Coworkee.OData.Transfer;

/// <summary>An export file for its owner and further <paramref name="Recipients"/>.</summary>
public sealed record DataExportMail(Guid OwnerId, IReadOnlyList<string> Recipients, string Name, ExportFile File);
