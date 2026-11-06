using System.Text.Json;

namespace Coworkee.Contracts.Theming;

/// <summary>A theme; <paramref name="IsChanged"/>: a built-in theme the organisation changed, which it can reset.</summary>
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
    bool IsPublished = true,
    bool IsChanged = false);
