namespace Coworkee.Contracts.Data;

public sealed record ImportResult(int Imported, IReadOnlyList<ImportRowError> Errors);

public sealed record ImportRowError(int Row, string Message);
