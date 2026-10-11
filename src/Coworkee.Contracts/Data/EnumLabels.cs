using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Coworkee.Contracts.Data;

/// <summary>
/// The English label of an enum member, a key for localization: its [Display] name, else its [Description], else the member name in words
/// ("InTransit" reads "In transit"). Server facets and client filters use the same text, so one translation covers both.
/// </summary>
public static partial class EnumLabels
{
    public static string Of(Enum value) => Of(value.GetType(), value.ToString());

    /// <summary>The label of the member <paramref name="name"/> of <paramref name="type"/>; flag combinations join their members' labels.</summary>
    public static string Of(Type type, string name)
    {
        if (type.GetField(name, BindingFlags.Public | BindingFlags.Static) is not { } field)
        {
            return string.Join(", ", name.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(n => type.GetField(n) is null ? n : Of(type, n)));
        }

        return field.GetCustomAttribute<DisplayAttribute>()?.GetName() is { Length: > 0 } display ? display
            : field.GetCustomAttribute<DescriptionAttribute>()?.Description is { Length: > 0 } description ? description
            : Humanize(name);
    }

    /// <summary>The color of a member from <see cref="FacetColorAttribute"/>, e.g. for facet swatches; <c>null</c> without one.</summary>
    public static string? ColorOf(Type type, string name) => type.GetField(name, BindingFlags.Public | BindingFlags.Static)?.GetCustomAttribute<FacetColorAttribute>()?.Color;

    /// <summary>"ShowLogoInNav" becomes "Show logo in nav", "HTTPProxy" stays "HTTP proxy".</summary>
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
