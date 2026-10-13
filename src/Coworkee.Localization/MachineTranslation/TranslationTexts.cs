using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Coworkee.Contracts.Localization;

namespace Coworkee.Localization.MachineTranslation;

/// <summary>Keeps placeholders like {0} out of the translation and maps cultures to the language codes of the services.</summary>
public static partial class TranslationTexts
{
    public static string ToHtml(string text) =>
        Placeholder().Replace(WebUtility.HtmlEncode(text), m => $"<span class=\"notranslate\" translate=\"no\">{m.Value}</span>");

    public static string FromHtml(string text) => WebUtility.HtmlDecode(Span().Replace(text, "$1"));

    public static string ToXml(string text) => Placeholder().Replace(WebUtility.HtmlEncode(text), m => $"<x>{m.Value}</x>");

    public static string FromXml(string text) => WebUtility.HtmlDecode(Keep().Replace(text, "$1"));

    /// <summary>Whether the translation has the same placeholders as the text; a translation that lost one would break string.Format.</summary>
    public static bool KeepsPlaceholders(string text, string translation) =>
        Placeholders(text).SequenceEqual(Placeholders(translation), StringComparer.Ordinal);

    /// <summary>The ISO 639-1 code of a culture, e.g. "fr" for fr-CH.</summary>
    public static string Language(string culture) => CultureInfo.GetCultureInfo(culture).TwoLetterISOLanguageName;

    public static bool IsTraditionalChinese(string culture) => ShippedCultures.IsTraditionalChinese(culture);

    private static IEnumerable<string> Placeholders(string text) => Placeholder().Matches(text).Select(m => m.Value).Order(StringComparer.Ordinal);

    [GeneratedRegex(@"\{\d+(:[^}]*)?\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"<span[^>]*>\s*(\{[^<]*\})\s*</span>")]
    private static partial Regex Span();

    [GeneratedRegex(@"<x>\s*(\{[^<]*\})\s*</x>")]
    private static partial Regex Keep();
}
