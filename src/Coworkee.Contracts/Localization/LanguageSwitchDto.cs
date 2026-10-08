namespace Coworkee.Contracts.Localization;

public sealed record SetLanguageEnabledRequest(bool Enabled);

/// <summary>The language after switching, and how many missing texts the translator filled in.</summary>
public sealed record LanguageSwitchDto(LanguageDto Language, int Translated, bool TranslatorAvailable);

public sealed record TranslateMissingDto(int Translated, bool TranslatorAvailable);
