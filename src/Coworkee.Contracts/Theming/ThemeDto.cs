using System.Text.Json;

namespace Coworkee.Contracts.Theming;

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
