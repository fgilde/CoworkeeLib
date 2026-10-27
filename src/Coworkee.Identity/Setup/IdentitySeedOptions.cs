namespace Coworkee.Identity.Setup;

public sealed class IdentitySeedOptions
{
    public string TenantName { get; set; } = "Default";

    public List<SeedRole> Roles { get; } = [];

    public List<SeedUser> Users { get; } = [];

    /// <summary>Name of a root group the seed creates and makes the default group, which new users (the seeded ones too) join; null for none.</summary>
    public string? DefaultGroup { get; set; }

    /// <summary>System wide settings stored with the setup, e.g. ["Theme.Mode"] = "dark".</summary>
    public Dictionary<string, string?> Settings { get; } = new(StringComparer.Ordinal);
}
