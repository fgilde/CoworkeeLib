namespace Coworkee.Contracts.Data;

public sealed record ImportColumnDto(int Index, string Header, IReadOnlyList<string?> Samples, string? SuggestedField);
