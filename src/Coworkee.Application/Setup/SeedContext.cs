namespace Coworkee.Application.Setup;

/// <param name="TenantId">The tenant of this call; null for <see cref="SeedTarget.Host"/>.</param>
/// <param name="IsDevelopment">A development run: the demo users are seeded (in the api: the Development environment).</param>
/// <param name="IsNew">
/// The system was set up in this run (by the identity seed or the setup wizard), or the tenant was just created. Seed what users own (sample records)
/// only then, so it does not come back after they deleted it.
/// </param>
public sealed record SeedContext(Guid? TenantId, bool IsDevelopment, bool IsNew);
