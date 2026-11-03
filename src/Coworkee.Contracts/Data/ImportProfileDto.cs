namespace Coworkee.Contracts.Data;

/// <summary>A saved mapping of an entity set: column header to field, reused for files with the same headers.</summary>
public sealed record ImportProfileDto(Guid Id, string EntitySet, string Name, IReadOnlyDictionary<string, string> Columns, ImportMode Mode, string? KeyField, string? Culture);
