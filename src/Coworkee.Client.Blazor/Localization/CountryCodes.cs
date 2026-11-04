using System.Collections.Concurrent;
using System.Globalization;

namespace Coworkee.Client.Blazor.Localization;

/// <summary>Countries as the ISO 3166 alpha-2 code of their flag: from a code (alpha-2 or alpha-3) or a name in English, the own language or the UI language.</summary>
public static class CountryCodes
{
    // the flags in wwwroot/flags/4x3; a browser loads only the culture data of its language, so codes must not depend on RegionInfo
    private static readonly HashSet<string> Flags = new(
        ("AD AE AF AG AI AL AM AO AQ AR AS AT AU AW AX AZ BA BB BD BE BF BG BH BI BJ BL BM BN BO BQ BR BS BT BV BW BY BZ CA CC CD CF CG CH CI CK CL CM CN CO CP CR CU CV CW CX CY CZ "
        + "DE DG DJ DK DM DO DZ EC EE EG EH ER ES ET EU FI FJ FK FM FO FR GA GB GD GE GF GG GH GI GL GM GN GP GQ GR GS GT GU GW GY HK HM HN HR HT HU IC ID IE IL IM IN IO IQ IR IS IT "
        + "JE JM JO JP KE KG KH KI KM KN KP KR KW KY KZ LA LB LC LI LK LR LS LT LU LV LY MA MC MD ME MF MG MH MK ML MM MN MO MP MQ MR MS MT MU MV MW MX MY MZ NA NC NE NF NG NI NL NO "
        + "NP NR NU NZ OM PA PC PE PF PG PH PK PL PM PN PR PS PT PW PY QA RE RO RS RU RW SA SB SC SD SE SG SH SI SJ SK SL SM SN SO SR SS ST SV SX SY SZ TC TD TF TG TH TJ TK TL TM TN "
        + "TO TR TT TV TW TZ UA UG UM UN US UY UZ VA VC VE VG VI VN VU WF WS XK YE YT ZA ZM ZW").Split(' '),
        StringComparer.OrdinalIgnoreCase);

    // names per UI language: RegionInfo.DisplayName follows it
    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> Names = new(StringComparer.Ordinal);

    /// <summary>Whether the library ships a flag for the alpha-2 <paramref name="code"/>.</summary>
    public static bool HasFlag(string? code) => code is { Length: 2 } && Flags.Contains(code);

    /// <summary>The upper-case alpha-2 code of <paramref name="value"/>; <c>null</c> for anything that is no country with a flag.</summary>
    public static string? Resolve(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (text.Length == 2)
        {
            return HasFlag(text) ? text.ToUpperInvariant() : null;
        }

        return Names.GetOrAdd(CultureInfo.CurrentUICulture.Name, _ => Build()).GetValueOrDefault(text);
    }

    private static Dictionary<string, string> Build()
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            RegionInfo region;
            try
            {
                region = new RegionInfo(culture.Name);
            }
            catch (ArgumentException)
            {
                continue;
            }

            var code = region.TwoLetterISORegionName.ToUpperInvariant();
            if (!HasFlag(code))
            {
                continue;
            }

            foreach (var name in new[] { region.ThreeLetterISORegionName, region.EnglishName, region.DisplayName, region.NativeName })
            {
                if (name.Length > 2)
                {
                    names.TryAdd(name, code);
                }
            }
        }

        return names;
    }
}
