namespace Coworkee.Contracts.Data;

public sealed record ImportPreviewRowDto(int Row, string Action, IReadOnlyDictionary<string, string?> Values, IReadOnlyList<string> Errors);
