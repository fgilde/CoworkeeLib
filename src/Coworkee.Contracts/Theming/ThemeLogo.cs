using System.Text;

namespace Coworkee.Contracts.Theming;

/// <summary>A theme's logo is SVG markup or a base64 image data URL (png, jpeg, webp, svg); this reads either as bytes and content type, or as data URL.</summary>
public static class ThemeLogo
{
    public const int MaxBytes = 512 * 1024;

    private const int MaxBase64Length = (MaxBytes + 2) / 3 * 4;

    public const string Svg = "image/svg+xml";

    public static readonly IReadOnlyList<string> ContentTypes = ["image/png", "image/jpeg", "image/webp", Svg];

    public static bool IsSvg(string contentType) => contentType == Svg;

    public static bool TryRead(string? logo, out byte[] bytes, out string contentType)
    {
        bytes = [];
        contentType = string.Empty;
        var text = logo?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        if (text.StartsWith("<svg", StringComparison.OrdinalIgnoreCase) || text.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
        {
            bytes = Encoding.UTF8.GetBytes(text);
            contentType = Svg;
            return true;
        }

        const string Prefix = "data:";
        const string Base64 = ";base64,";
        var split = text.IndexOf(Base64, StringComparison.Ordinal);
        if (!text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) || split < 0)
        {
            return false;
        }

        var type = text[Prefix.Length..split].ToLowerInvariant();
        var payload = text[(split + Base64.Length)..];
        if (payload.Length > MaxBase64Length || !ContentTypes.Contains(type))
        {
            return false;
        }

        var buffer = new byte[payload.Length * 3 / 4 + 3];
        if (!Convert.TryFromBase64String(payload, buffer, out var written))
        {
            return false;
        }

        bytes = buffer[..written];
        contentType = type;
        return true;
    }

    /// <summary>The logo as value for an img src; null when it is empty or neither SVG markup nor an image data URL.</summary>
    public static string? ToDataUrl(string? logo) => TryRead(logo, out var bytes, out var contentType)
        ? $"data:{contentType};base64,{Convert.ToBase64String(bytes)}"
        : null;
}
