namespace Coworkee.Contracts.Data;

public sealed record SaveImportProfileRequest(string Name, IReadOnlyDictionary<string, string> Columns, ImportMode Mode, string? KeyField, string? Culture);
