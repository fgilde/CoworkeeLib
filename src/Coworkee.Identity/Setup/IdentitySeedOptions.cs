namespace Coworkee.Identity.Setup;

public sealed class IdentitySeedOptions
{
    public string TenantName { get; set; } = "Default";

    public List<SeedRole> Roles { get; } = [];

    public List<SeedUser> Users { get; } = [];
}
