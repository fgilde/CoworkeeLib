using System.Globalization;

namespace Coworkee.Application.Localization;

/// <summary>Translates English texts, the keys of the app's texts, into a language; null when there is no translation.</summary>
public interface ITextTranslator
{
    string? Translate(string text, CultureInfo culture);
}

public static class TextTranslatorExtensions
{
    /// <summary>The first translation of the translators, else the English text.</summary>
    public static string Translate(this IEnumerable<ITextTranslator> translators, string text, CultureInfo culture) =>
        translators.Select(t => t.Translate(text, culture)).FirstOrDefault(t => t is not null) ?? text;
}
