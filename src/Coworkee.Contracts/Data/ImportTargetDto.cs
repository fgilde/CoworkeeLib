namespace Coworkee.Contracts.Data;

/// <summary>An entity set rows can be imported into. <see cref="KeyFields"/> find existing rows for update and upsert.</summary>
public sealed record ImportTargetDto(string EntitySet, IReadOnlyList<ImportFieldDto> Fields, bool CanUpdate, bool CanUndo, IReadOnlyList<string> KeyFields);
