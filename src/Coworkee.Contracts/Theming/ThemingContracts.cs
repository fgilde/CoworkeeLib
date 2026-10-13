using System.Text.Json;

namespace Coworkee.Contracts.Theming;

public static class ThemePermissions
{
    public const string GroupName = "Themes";
    public const string Manage = "Themes.Manage";
}

public static class ThemeSettings
{
    public const string Mode = "Theme.Mode";
    public const string ThemeId = "Theme.Id";
}

public sealed record ThemeDto(
    Guid Id,
    string Name,
    bool IsGlobal,
    bool IsDefault,
    JsonElement PaletteLight,
    JsonElement PaletteDark,
    JsonElement? Typography,
    JsonElement? LayoutProperties,
    string? Logo,
    string? CustomCss,
    int Revision,
    JsonElement? Shadows = null,
    JsonElement? Options = null,
    bool IsPublished = true);

/// <summary>The Keycloak login theme the API keeps in the app's design; <see cref="Configured"/> is false without a theme folder.</summary>
public sealed record KeycloakThemeStatusDto(bool Configured, string? ThemeName, DateTimeOffset? WrittenAt, string? Error);

public sealed record ThemeRequest(
    string Name,
    JsonElement PaletteLight,
    JsonElement PaletteDark,
    JsonElement? Typography,
    JsonElement? LayoutProperties,
    string? Logo,
    string? CustomCss,
    JsonElement? Shadows = null,
    JsonElement? Options = null,
    bool IsPublished = false);
