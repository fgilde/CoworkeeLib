namespace Coworkee.Application.Localization;

/// <summary>The default language of the current organisation, else of the system; the localization module provides it.</summary>
public interface IDefaultLanguage
{
    Task<string?> GetAsync(CancellationToken cancellationToken);
}

public static class DefaultLanguageExtensions
{
    /// <summary>The default language, or null when no module provides one.</summary>
    public static async Task<string?> GetAsync(this IEnumerable<IDefaultLanguage> languages, CancellationToken cancellationToken) =>
        languages.FirstOrDefault() is { } language ? await language.GetAsync(cancellationToken) : null;
}
