using Coworkee.Application.Setup;

namespace Coworkee.Identity.Setup;

/// <summary><see cref="CoworkeeMigrationsOptions.SyncRolesInDeployments"/>: the roles of the deployment seed and their grants, before the app's contributors.</summary>
internal sealed class DeploymentRolesSeed(IdentitySeeder seeder, IReadOnlyList<SeedRole> roles) : ISeedContributor
{
    public int Order => int.MinValue;

    public SeedEnvironments Environments => SeedEnvironments.Deployment;

    public SeedTarget Target => SeedTarget.Host;

    public Task SeedAsync(SeedContext context, CancellationToken cancellationToken) => seeder.SyncRolesAsync(roles, cancellationToken);
}
