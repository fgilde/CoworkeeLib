namespace Coworkee.Contracts.Data;

public sealed record ImportResult(int Imported, IReadOnlyList<ImportRowError> Errors);
