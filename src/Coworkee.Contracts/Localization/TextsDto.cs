namespace Coworkee.Contracts.Localization;

/// <summary>All texts of a culture: module defaults with the edits of the administrators on top.</summary>
public sealed record TextsDto(string Culture, IReadOnlyDictionary<string, string> Texts);
