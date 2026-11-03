namespace Coworkee.Application.Localization;

public static class DefaultLanguageExtensions
{
    /// <summary>The default language, or null when no module provides one.</summary>
    public static async Task<string?> GetAsync(this IEnumerable<IDefaultLanguage> languages, CancellationToken cancellationToken) =>
        languages.FirstOrDefault() is { } language ? await language.GetAsync(cancellationToken) : null;
}
