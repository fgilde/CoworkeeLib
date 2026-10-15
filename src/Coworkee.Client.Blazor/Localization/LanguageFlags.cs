using System.Globalization;

namespace Coworkee.Client.Blazor.Localization;

/// <summary>The flag emoji of a culture's country, e.g. 🇩🇪 for de; empty when the culture has no country.</summary>
public static class LanguageFlags
{
    public static string For(string culture)
    {
        try
        {
            var specific = culture.Contains('-') && !culture.EndsWith("-Hant", StringComparison.OrdinalIgnoreCase) && !culture.EndsWith("-Hans", StringComparison.OrdinalIgnoreCase)
                ? culture
                : CultureInfo.CreateSpecificCulture(culture).Name;
            var region = new RegionInfo(specific).TwoLetterISORegionName;
            return region.Length == 2 && region.All(char.IsAsciiLetterUpper) ? string.Concat(region.Select(c => char.ConvertFromUtf32(0x1F1E6 + c - 'A'))) : string.Empty;
        }
        catch (ArgumentException)
        {
            // ponytail: a browser without the culture's ICU data shows the code alone
            return string.Empty;
        }
    }
}
