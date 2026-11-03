using System.Text.Json;

namespace Coworkee.Contracts.Theming;

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
