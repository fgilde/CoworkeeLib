using System.Globalization;

namespace Coworkee.Application.Localization;

/// <summary>Translates English texts, the keys of the app's texts, into a language; null when there is no translation.</summary>
public interface ITextTranslator
{
    string? Translate(string text, CultureInfo culture);
}
