using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace Coworkee.AuthServer;

/// <summary>Texts of the account pages in the request language (Accept-Language); the English text is the key, Texts/{language}.json translates it.</summary>
public static class AuthTexts
{
    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> Languages = new(StringComparer.OrdinalIgnoreCase);

    public static string T(string text) => Find(text, CultureInfo.CurrentUICulture) ?? text;

    public static string? Find(string text, CultureInfo culture) =>
        Languages.GetOrAdd(culture.TwoLetterISOLanguageName, Load).TryGetValue(text, out var translated) ? translated : null;

    public static string T(string text, params object?[] arguments) => string.Format(CultureInfo.CurrentCulture, T(text), arguments);

    private static IReadOnlyDictionary<string, string> Load(string language)
    {
        using var stream = typeof(AuthTexts).Assembly.GetManifestResourceStream($"Coworkee.AuthServer.Texts.{language}.json");
        return stream is null
            ? new Dictionary<string, string>()
            : JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
    }
}

/// <summary>The account page texts for server messages such as the ones of ASP.NET Core Identity.</summary>
internal sealed class AuthTextTranslator : Application.Localization.ITextTranslator
{
    public string? Translate(string text, CultureInfo culture) => AuthTexts.Find(text, culture);
}
