namespace Coworkee.Identity.Setup;

public sealed class IdentitySeedOptions
{
    public string TenantName { get; set; } = "Default";

    public List<SeedRole> Roles { get; } = [];

    public List<SeedUser> Users { get; } = [];

    /// <summary>System wide settings stored with the setup, e.g. ["Theme.Mode"] = "dark".</summary>
    public Dictionary<string, string?> Settings { get; } = new(StringComparer.Ordinal);
}
