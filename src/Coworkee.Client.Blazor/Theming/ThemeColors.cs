using System.Text.Json;
using Coworkee.Contracts.Theming;

namespace Coworkee.Client.Blazor.Theming;

/// <summary>The palette colors of a theme for previews and swatches, as values safe to put into a style attribute.</summary>
public static class ThemeColors
{
    /// <summary>The color <paramref name="key"/> (e.g. "Primary") of the light or dark palette; <c>null</c> when unset or not a plain color.</summary>
    public static string? Of(ThemeDto theme, string key, bool dark = false)
    {
        var palette = dark ? theme.PaletteDark : theme.PaletteLight;
        // only plain color values go into the style attribute
        return palette.ValueKind == JsonValueKind.Object && palette.TryGetProperty(key, out var value)
            && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 and <= 40 } color
            && color.All(c => char.IsAsciiLetterOrDigit(c) || c is '#' or '(' or ')' or ',' or '.' or ' ' or '%')
                ? color
                : null;
    }

    /// <summary>The colors among <paramref name="keys"/> the palette sets, in order.</summary>
    public static IEnumerable<string> All(ThemeDto theme, bool dark, params string[] keys) =>
        keys.Select(k => Of(theme, k, dark)).OfType<string>();
}
