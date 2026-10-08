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
    string? LogoSvg,
    string? CustomCss,
    int Revision,
    JsonElement? Shadows = null,
    JsonElement? Options = null,
    bool IsPublished = true);

public sealed record ThemeRequest(
    string Name,
    JsonElement PaletteLight,
    JsonElement PaletteDark,
    JsonElement? Typography,
    JsonElement? LayoutProperties,
    string? LogoSvg,
    string? CustomCss,
    JsonElement? Shadows = null,
    JsonElement? Options = null,
    bool IsPublished = false);
