using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Coworkee.Client.Blazor.Localization;

/// <summary>Object edit labels without an explicit label: the [Display] name, else "ShowLogoInNav" becomes "Show logo in nav".</summary>
public static partial class PropertyLabels
{
    public static string For(PropertyInfo property) =>
        property.GetCustomAttribute<DisplayAttribute>()?.GetName() is { Length: > 0 } name ? name : Humanize(property.Name);

    public static string Humanize(string name)
    {
        var words = Words().Matches(name).Select(m => m.Value).ToList();
        return words.Count == 0
            ? name
            : string.Join(' ', words.Select((w, i) => i == 0 || w.Length > 1 && !char.IsLower(w[1]) ? w : w.ToLowerInvariant()));
    }

    [GeneratedRegex("[A-Z]+[0-9]*(?![a-z])|[A-Z]?[a-z]+[0-9]*|[0-9]+")]
    private static partial Regex Words();
}
