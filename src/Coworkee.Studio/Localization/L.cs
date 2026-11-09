using System.Globalization;
using Avalonia.Markup.Xaml;

namespace Coworkee.Studio.Localization;

/// <summary>The English text is the key; the other languages come from their dictionaries (<see cref="German"/>, <see cref="French"/>, ...), anything without a translation stays English.</summary>
public sealed class L(string text) : MarkupExtension
{
    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> Translations = new()
    {
        ["de"] = German.Texts,
        ["fr"] = French.Texts,
        ["es"] = Spanish.Texts,
        ["it"] = Italian.Texts,
        ["nl"] = Dutch.Texts,
        ["pl"] = Polish.Texts,
        ["pt"] = Portuguese.Texts,
    };

    /// <summary>The languages of the Studio, English first, each in its own name.</summary>
    public static IReadOnlyList<(string Code, string Name)> Languages { get; } =
    [
        ("en", "English"), ("de", "Deutsch"), ("fr", "Français"), ("es", "Español"),
        ("it", "Italiano"), ("nl", "Nederlands"), ("pl", "Polski"), ("pt", "Português"),
    ];

    /// <summary>Two-letter code of the current language.</summary>
    public static string Language { get; private set; } = SystemLanguage;

    public static bool IsGerman => Language == "de";

    private static string SystemLanguage => Supported(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName) ?? "en";

    public static string Get(string text) =>
        Translations.TryGetValue(Language, out var texts) && texts.TryGetValue(text, out var translated) ? translated : text;

    public static string Format(string text, params object?[] args) => string.Format(CultureInfo.CurrentCulture, Get(text), args);

    /// <summary>Switches the language; null or empty takes the system's language when the Studio has it, else English.</summary>
    public static void Use(string? language) =>
        Language = string.IsNullOrEmpty(language) ? SystemLanguage : Supported(language) ?? "en";

    private static string? Supported(string language) =>
        Languages.Select(l => l.Code).FirstOrDefault(code => string.Equals(code, language, StringComparison.OrdinalIgnoreCase));

    public override object ProvideValue(IServiceProvider serviceProvider) => Get(text);
}
