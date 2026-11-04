using System.Globalization;

namespace Coworkee.Client.Blazor.Localization;

/// <summary>The country whose flag stands for a culture: its own region ("de-AT" → AT), else the usual country of the language ("en" → GB).</summary>
public static class LanguageFlags
{
    // languages spoken in several countries, and the shipped ones, so a browser without their culture data still finds the flag; the shipped pt texts are Brazilian
    private static readonly Dictionary<string, string> Defaults = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = "GB", ["ar"] = "SA", ["zh"] = "CN", ["zh-Hans"] = "CN", ["zh-Hant"] = "TW", ["ja"] = "JP", ["ko"] = "KR", ["km"] = "KH", ["uk"] = "UA",
        ["el"] = "GR", ["cs"] = "CZ", ["da"] = "DK", ["sv"] = "SE", ["hr"] = "HR", ["id"] = "ID", ["pt"] = "BR", ["nl"] = "NL", ["de"] = "DE", ["fr"] = "FR",
        ["es"] = "ES", ["it"] = "IT", ["pl"] = "PL", ["ru"] = "RU", ["tr"] = "TR", ["bg"] = "BG", ["hi"] = "IN", ["he"] = "IL", ["fa"] = "IR", ["vi"] = "VN",
        ["th"] = "TH", ["ms"] = "MY", ["nb"] = "NO", ["nn"] = "NO", ["no"] = "NO", ["fi"] = "FI", ["et"] = "EE", ["sr"] = "RS", ["sw"] = "KE", ["ur"] = "PK",
        ["bn"] = "BD", ["ta"] = "IN", ["ca"] = "ES", ["eu"] = "ES", ["gl"] = "ES",
    };

    /// <summary>The alpha-2 code of the flag of <paramref name="culture"/>; <paramref name="overrides"/> (CoworkeeClientOptions.LanguageRegions) win over the defaults.</summary>
    public static string? Region(string? culture, IReadOnlyDictionary<string, string>? overrides = null)
    {
        if (string.IsNullOrWhiteSpace(culture))
        {
            return null;
        }

        var parts = culture.Split('-');
        var language = parts.Length > 1 && parts[1].Length == 4 ? $"{parts[0]}-{parts[1]}" : parts[0];
        foreach (var candidate in new[]
        {
            overrides?.GetValueOrDefault(culture),
            parts.Length > 1 ? parts[^1] : null,
            overrides?.GetValueOrDefault(language),
            overrides?.GetValueOrDefault(parts[0]),
            Defaults.GetValueOrDefault(language),
            Defaults.GetValueOrDefault(parts[0]),
        })
        {
            if (CountryCodes.HasFlag(candidate))
            {
                return candidate!.ToUpperInvariant();
            }
        }

        try
        {
            var region = new RegionInfo(CultureInfo.CreateSpecificCulture(culture).Name).TwoLetterISORegionName;
            return CountryCodes.HasFlag(region) ? region.ToUpperInvariant() : null;
        }
        catch (ArgumentException)
        {
            // a browser without the culture's ICU data, or no culture at all
            return null;
        }
    }

    /// <summary>The flag emoji of a culture's country, e.g. 🇩🇪 for de, for plain text; empty when the culture has no country.</summary>
    public static string For(string culture) =>
        Region(culture) is { } region ? string.Concat(region.Select(c => char.ConvertFromUtf32(0x1F1E6 + c - 'A'))) : string.Empty;
}
