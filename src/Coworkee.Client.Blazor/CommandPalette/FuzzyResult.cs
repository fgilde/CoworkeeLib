namespace Coworkee.Client.Blazor.CommandPalette;

public sealed record FuzzyResult(int Score, IReadOnlyList<int> Positions);
