using System.Globalization;
using Coworkee.Localization.Domain;
using Coworkee.Localization.Resources;

namespace Coworkee.Localization.Languages;

/// <summary>The languages of <see cref="LocalizationOptions"/>, offered until administrators store their own.</summary>
internal static class InitialLanguages
{
    public static IEnumerable<Language> Create(LocalizationOptions options, LocalizationResources resources) =>
        options.Initial(resources.Cultures).Select(l => new Language { Culture = l.Culture, Name = NativeName(l.Culture), IsDefault = l.IsDefault });

    public static string NativeName(string culture)
    {
        var name = CultureInfo.GetCultureInfo(culture).NativeName;
        return char.ToUpper(name[0], CultureInfo.InvariantCulture) + name[1..];
    }

    public static bool IsCulture(string culture)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(culture) && CultureInfo.GetCultureInfo(culture.Trim(), predefinedOnly: true) is not null;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }
}
