namespace Coworkee.Identity.Groups;

/// <summary>A row of the group import: <c>ParentPath</c> is the slash-joined names of the parents from the root ("Customer A/Hamburg"), empty for a root group.</summary>
public sealed record GroupImportRow(string Name, string? Description, string? ParentPath);
