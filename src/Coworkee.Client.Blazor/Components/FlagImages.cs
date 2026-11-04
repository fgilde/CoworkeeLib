using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components;

/// <summary>
/// The country flags shipped with the library (flag-icons, MIT, in wwwroot/flags/4x3), as a plain img so render-hot places such as table
/// cells and facet lists need no component per value.
/// </summary>
public static class FlagImages
{
    public const string Folder = "_content/Coworkee.Client.Blazor/flags/4x3/";

    private const string Style = "display:inline-block;vertical-align:-0.15em;border-radius:2px;box-shadow:0 0 0 1px rgba(0,0,0,.12);object-fit:cover";

    /// <summary>The image of an ISO 3166 alpha-2 code (see Localization.CountryCodes.Resolve), e.g. ".../de.svg" for "DE".</summary>
    public static string Src(string code) => Folder + code.ToLowerInvariant() + ".svg";

    /// <summary>The flag, <paramref name="height"/> pixels high in its 4:3 shape; an empty <paramref name="alt"/> marks it as decoration next to its text.</summary>
    public static RenderFragment Image(string code, int height, string alt, string? style = null) => builder =>
    {
        builder.OpenElement(0, "img");
        builder.AddAttribute(1, "src", Src(code));
        builder.AddAttribute(2, "alt", alt);
        builder.AddAttribute(3, "width", height * 4 / 3);
        builder.AddAttribute(4, "height", height);
        builder.AddAttribute(5, "loading", "lazy");
        builder.AddAttribute(6, "class", "cw-flag");
        builder.AddAttribute(7, "style", style is null ? Style : Style + ";" + style);
        builder.CloseElement();
    };
}
