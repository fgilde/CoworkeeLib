using System.Globalization;
using Avalonia.Markup.Xaml;

namespace Coworkee.Studio.Localization;

/// <summary>The English text is the key; German comes from <see cref="German"/>, anything without a translation stays English.</summary>
public sealed class L(string text) : MarkupExtension
{
    public static bool IsGerman { get; set; } = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de";

    public static string Get(string text) => IsGerman && German.Texts.TryGetValue(text, out var german) ? german : text;

    public static string Format(string text, params object?[] args) => string.Format(CultureInfo.CurrentCulture, Get(text), args);

    public static void Use(string? language) =>
        IsGerman = string.IsNullOrEmpty(language) ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" : language == "de";

    public override object ProvideValue(IServiceProvider serviceProvider) => Get(text);
}
