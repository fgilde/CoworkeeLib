using System.Text.Json;
using System.Text.RegularExpressions;
using Coworkee.Contracts.Theming;

namespace Coworkee.Theming;

public static partial class ThemeValidation
{
    private const int MaxCss = 20_000;
    private const int MaxSvg = 100_000;

    private static readonly string[] ForbiddenCss = ["</style", "<script", "@import", "expression(", "javascript:", "behavior:", "-moz-binding"];
    private static readonly string[] ForbiddenSvg =
    [
        "<script", "javascript:", "<foreignobject", "<iframe", "<embed", "<object", "<!entity", "<!doctype", "data:text/html",
        "&", "<style", "<image", "<use", "<a", "<set", "<feimage", "<handler", "<listener",
    ];

    public static string? Validate(ThemeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 100)
        {
            return "Name is required and must not exceed 100 characters.";
        }

        return Palette(request.PaletteLight, "PaletteLight")
            ?? Palette(request.PaletteDark, "PaletteDark")
            ?? Tokens(request.Typography, "Typography")
            ?? Tokens(request.LayoutProperties, "LayoutProperties")
            ?? Tokens(request.Shadows, "Shadows")
            ?? Tokens(request.Options, "Options")
            ?? Css(request.CustomCss)
            ?? Svg(request.LogoSvg);
    }

    private static string? Palette(JsonElement palette, string field)
    {
        if (palette.ValueKind != JsonValueKind.Object)
        {
            return $"{field} must be a JSON object.";
        }

        foreach (var property in palette.EnumerateObject())
        {
            var valid = property.Value.ValueKind switch
            {
                JsonValueKind.String => Color().IsMatch(property.Value.GetString()!),
                JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null => true,
                _ => false,
            };
            if (!valid)
            {
                return $"{field}.{property.Name} is not a valid color.";
            }
        }

        return null;
    }

    private static string? Tokens(JsonElement? tokens, string field)
    {
        if (tokens is not { } element || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return $"{field} must be a JSON object.";
        }

        return Strings(element).Any(Unsafe) ? $"{field} contains characters that are not allowed." : null;
    }

    private static IEnumerable<string> Strings(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => [element.GetString()!],
        JsonValueKind.Object => element.EnumerateObject().SelectMany(p => Strings(p.Value)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(Strings),
        _ => [],
    };

    private static bool Unsafe(string value) =>
        value.IndexOfAny([';', '{', '}', '<', '>']) >= 0
        || value.Contains("url(", StringComparison.OrdinalIgnoreCase)
        || value.Contains("expression(", StringComparison.OrdinalIgnoreCase);

    private static string? Css(string? css)
    {
        if (string.IsNullOrEmpty(css))
        {
            return null;
        }

        return css.Length > MaxCss || ForbiddenCss.Any(f => css.Contains(f, StringComparison.OrdinalIgnoreCase))
            ? "CustomCss is too long or contains forbidden constructs."
            : null;
    }

    private static string? Svg(string? svg)
    {
        if (string.IsNullOrEmpty(svg))
        {
            return null;
        }

        var trimmed = svg.Trim();
        var valid = trimmed.Length <= MaxSvg
            && trimmed.StartsWith("<svg", StringComparison.OrdinalIgnoreCase)
            && trimmed.EndsWith("</svg>", StringComparison.OrdinalIgnoreCase)
            && !ForbiddenSvg.Any(f => trimmed.Contains(f, StringComparison.OrdinalIgnoreCase))
            && !EventAttribute().IsMatch(trimmed)
            && !ExternalReference().IsMatch(trimmed);
        return valid ? null : "LogoSvg must be a plain SVG without scripts, event handlers or embedded documents.";
    }

    [GeneratedRegex(@"^(#([0-9a-fA-F]{3,4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})|(rgb|rgba|hsl|hsla)\(\s*[0-9.]+%?\s*(,\s*[0-9.]+%?\s*){2,3}\)|transparent)$")]
    private static partial Regex Color();

    [GeneratedRegex(@"[\s/""']on[a-z]+\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex EventAttribute();

    [GeneratedRegex(@"href\s*=\s*[""']?(?!#)", RegexOptions.IgnoreCase)]
    private static partial Regex ExternalReference();
}
