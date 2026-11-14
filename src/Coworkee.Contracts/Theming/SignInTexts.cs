using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Coworkee.Contracts.Configuration;

namespace Coworkee.Contracts.Theming;

/// <summary>
/// Texts of the sign-in page instead of its built-in heading: <see cref="Title"/>, <see cref="Subtitle"/> and <see cref="Notice"/> for every language,
/// the dictionaries ("de": "Willkommen") optionally per language. Plain text: the pages encode it, a line break in the notice stays one.
/// </summary>
public sealed class SignInTexts
{
    private const string Group = "Texts";
    private const string Every = "For every language without its own text; empty keeps the built-in one.";

    [Display(Name = "Title", GroupName = Group, Description = Every)]
    public string? Title { get; set; }

    [Translations]
    [Display(Name = "Title in other languages", GroupName = Group)]
    public Dictionary<string, string> Titles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [Display(Name = "Subtitle", GroupName = Group, Description = Every)]
    public string? Subtitle { get; set; }

    [Translations]
    [Display(Name = "Subtitle in other languages", GroupName = Group)]
    public Dictionary<string, string> Subtitles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [Display(Name = "Notice", GroupName = Group, Description = "A hint above the sign-in form, e.g. maintenance or whom to ask for an account. " + Every)]
    public string? Notice { get; set; }

    [Translations]
    [Display(Name = "Notice in other languages", GroupName = Group)]
    public Dictionary<string, string> Notices { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The title in <paramref name="culture"/> ("de-AT", then "de"), else <see cref="Title"/>; null when neither is set.</summary>
    public string? TitleIn(CultureInfo culture) => In(Titles, Title, culture);

    public string? SubtitleIn(CultureInfo culture) => In(Subtitles, Subtitle, culture);

    public string? NoticeIn(CultureInfo culture) => In(Notices, Notice, culture);

    private static string? In(Dictionary<string, string>? texts, string? fallback, CultureInfo culture) =>
        Text(texts, culture.Name) ?? Text(texts, culture.TwoLetterISOLanguageName) ?? (string.IsNullOrWhiteSpace(fallback) ? null : fallback.Trim());

    private static string? Text(Dictionary<string, string>? texts, string culture) =>
        texts is not null && texts.FirstOrDefault(t => string.Equals(t.Key, culture, StringComparison.OrdinalIgnoreCase)).Value is { } text && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;
}
