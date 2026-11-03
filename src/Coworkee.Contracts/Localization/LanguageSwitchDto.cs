namespace Coworkee.Contracts.Localization;

/// <summary>The language after switching, and how many missing texts the translator filled in.</summary>
public sealed record LanguageSwitchDto(LanguageDto Language, int Translated, bool TranslatorAvailable);
