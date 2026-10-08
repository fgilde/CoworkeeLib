namespace Coworkee.Contracts.Localization;

/// <summary>Sets the text of a key in a culture; an empty value goes back to the module default.</summary>
public sealed record SetTranslationRequest(string Culture, string Key, string? Value);
