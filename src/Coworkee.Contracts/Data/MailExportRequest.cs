namespace Coworkee.Contracts.Data;

/// <summary>An export written in the background and mailed to the requester and <paramref name="Recipients"/>; <paramref name="Query"/> is the export's query string.</summary>
public sealed record MailExportRequest(string? Query, IReadOnlyList<string>? Recipients = null, string? FileName = null);
