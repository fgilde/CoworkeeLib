using System.Globalization;

namespace Coworkee.Application.Localization;

public static class TextTranslatorExtensions
{
    /// <summary>The first translation of the translators, else the English text.</summary>
    public static string Translate(this IEnumerable<ITextTranslator> translators, string text, CultureInfo culture) =>
        translators.Select(t => t.Translate(text, culture)).FirstOrDefault(t => t is not null) ?? text;
}
