namespace Coworkee.Application.Setup;

/// <summary>
/// Data the app keeps in its database. Runs in the migrations process on every run, after the schema, the identity seed and the development users,
/// and in the api right after the setup wizard or a new tenant (only <see cref="SeedTarget.EachTenant"/> contributors of the modules there). Each call
/// has a DI scope and a transaction of its own and runs as the system with every permission. Must be idempotent: check, then add or change what is
/// missing. Register with <c>[RegisterAs(typeof(ISeedContributor))]</c> in a module or the migrations project.
/// </summary>
public interface ISeedContributor
{
    Task SeedAsync(SeedContext context, CancellationToken cancellationToken);

    /// <summary>Lower runs first; ties by type name.</summary>
    int Order => 0;

    /// <summary>Development runs (the demo users are seeded), deployments, or both.</summary>
    SeedEnvironments Environments => SeedEnvironments.Always;

    /// <summary>Once per tenant with the tenant set (default), or once per run without one.</summary>
    SeedTarget Target => SeedTarget.EachTenant;

    /// <summary>False: a failure is logged as a warning and the run goes on; true (default): a failure stops the run (exit code 1 in the migrations).</summary>
    bool Required => true;
}
