using Coworkee.Domain;

namespace Coworkee.Theming;

[Realtime(Contracts.Theming.ThemePermissions.Manage)]
public sealed class ThemeDefinition : AuditedAggregateRoot, IVersioned
{
    public required string Name { get; set; }

    public Guid? TenantId { get; set; }

    /// <summary>
    /// Set on an organisation's own version of a built-in theme: the organisation sees the built-in theme with this row's look (when
    /// <see cref="IsCustomized"/>) and publishing; lists and choices keep the built-in theme's id.
    /// </summary>
    public Guid? BuiltInId { get; set; }

    /// <summary>The organisation changed the built-in theme's look; without it the built-in look applies, also after the app updates it.</summary>
    public bool IsCustomized { get; set; }

    public bool IsDefault { get; set; }

    public required string PaletteLight { get; set; }

    public required string PaletteDark { get; set; }

    public string? Typography { get; set; }

    public string? LayoutProperties { get; set; }

    public string? Shadows { get; set; }

    /// <summary>The app's own theme options: navigation, logo, density.</summary>
    public string? Options { get; set; }

    /// <summary>Users can choose it; built-in themes always can.</summary>
    public bool IsPublished { get; set; }

    /// <summary>SVG markup or an image data URL; empty means the app's logo.</summary>
    public string? Logo { get; set; }

    public string? CustomCss { get; set; }

    public int Revision { get; set; }
}
