using System.Globalization;

namespace Coworkee.AuthServer;

/// <summary>The account page texts for server messages such as the ones of ASP.NET Core Identity.</summary>
internal sealed class AuthTextTranslator : Application.Localization.ITextTranslator
{
    public string? Translate(string text, CultureInfo culture) => AuthTexts.Find(text, culture);
}
