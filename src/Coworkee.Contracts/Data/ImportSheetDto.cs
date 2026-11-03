namespace Coworkee.Contracts.Data;

/// <summary>A parsed upload: what was detected and the columns with their first values and a suggested field.</summary>
public sealed record ImportSheetDto(Guid JobId, string FileName, int RowCount, string? Delimiter, string? Encoding, bool HasHeader, IReadOnlyList<ImportColumnDto> Columns);
