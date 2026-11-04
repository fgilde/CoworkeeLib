using System.Globalization;
using Coworkee.Client.Blazor.Localization;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components;

/// <summary>The flag of a country, e.g. <c>&lt;CountryFlag Code="DE" /&gt;</c>: an alpha-2 or alpha-3 code in any case, or a country name; nothing for an unknown one.</summary>
public partial class CountryFlag
{
    [Parameter] public string? Code { get; set; }

    /// <summary>Height in pixels; the flag is 4:3.</summary>
    [Parameter] public int Size { get; set; } = 16;

    /// <summary>The text of the image; the country's name by default. Pass "" where the name stands next to the flag.</summary>
    [Parameter] public string? Alt { get; set; }

    [Parameter] public string? Style { get; set; }

    private string? Resolved => CountryCodes.Resolve(Code);

    private static string NameOf(string code)
    {
        try
        {
            return new RegionInfo(code).DisplayName;
        }
        catch (ArgumentException)
        {
            return code;
        }
    }
}
