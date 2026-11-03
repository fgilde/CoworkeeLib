namespace Coworkee.Contracts.Theming;

/// <summary>What the Keycloak login theme shows; <see cref="LogoUrl"/> and the design's media address must be absolute web addresses, others are left out.</summary>
public sealed record KeycloakLoginThemeContent(string AppName, IReadOnlyDictionary<string, string> Light, IReadOnlyDictionary<string, string> Dark)
{
    /// <summary>The theme's logo (SVG markup or image data URL), a raster image is written as file next to the styles, SVG is embedded in them.</summary>
    public string? Logo { get; init; }

    /// <summary>The logo when <see cref="Logo"/> is empty.</summary>
    public string? LogoUrl { get; init; }

    public SignInDesign Design { get; init; } = new();
}
