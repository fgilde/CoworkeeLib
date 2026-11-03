namespace Coworkee.Contracts.Features;

public sealed record EditionRequest(string Name, string? Description, IReadOnlyDictionary<string, string>? Values);
