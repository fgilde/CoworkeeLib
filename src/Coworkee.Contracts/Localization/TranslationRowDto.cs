namespace Coworkee.Contracts.Localization;

/// <param name="Default">Text of the module resources for the culture, if any.</param>
/// <param name="Value">Text an administrator set; it wins over <paramref name="Default"/>.</param>
public sealed record TranslationRowDto(string Key, string? Default, string? Value);
