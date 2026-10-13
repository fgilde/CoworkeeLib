namespace Coworkee.Contracts.Localization;

/// <summary>Which shipped text file (<c>de.json</c>, <c>zh.json</c>) a culture reads: its own, then its language's, except that Traditional Chinese never reads the Simplified <c>zh</c>.</summary>
public static class ShippedCultures
{
    private static readonly string[] TraditionalChinese = ["Hant", "TW", "HK", "MO", "CHT"];

    public static IReadOnlyList<string> Files(string culture)
    {
        if (culture.IndexOf('-') is var dash and > 0 && !IsTraditionalChinese(culture))
        {
            return [culture, culture[..dash]];
        }

        return [culture];
    }

    public static bool IsTraditionalChinese(string culture)
    {
        var parts = culture.Split('-');
        return parts[0].Equals("zh", StringComparison.OrdinalIgnoreCase) && parts.Skip(1).Any(p => TraditionalChinese.Contains(p, StringComparer.OrdinalIgnoreCase));
    }
}
