namespace Coworkee.Identity.Setup;

public sealed class CoworkeeMigrationsOptions
{
    /// <summary>The seed of a local run: the development users and roles of the app.</summary>
    public Action<IdentitySeedOptions>? DevelopmentSeed { get; set; }

    /// <summary>The seed of a deployment with its first administrator (<see cref="InitialAdmin"/>); without it the administrator alone.</summary>
    public Action<IdentitySeedOptions, SeedUser>? DeploymentSeed { get; set; }

    /// <summary>Whether a run without first administrator seeds <see cref="DevelopmentSeed"/>; default: in the Development environment.</summary>
    public bool? DevelopmentUsers { get; set; }

    /// <summary>Tries to reach the database (3 seconds apart); default <c>Migrations:Attempts</c> or 20.</summary>
    public int Attempts { get; set; }

    /// <summary>Runs after the migration and the seed, before Keycloak is reconciled: the app's own data steps.</summary>
    public Func<IServiceProvider, CancellationToken, Task>? AfterSeed { get; set; }
}
