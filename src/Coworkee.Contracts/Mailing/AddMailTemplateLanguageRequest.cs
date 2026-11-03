namespace Coworkee.Contracts.Mailing;

/// <summary>Adds a language variant prefilled from <paramref name="From"/>; <paramref name="Translate"/> machine translates it when a translation service is configured.</summary>
public sealed record AddMailTemplateLanguageRequest(string From, bool Translate = false);
