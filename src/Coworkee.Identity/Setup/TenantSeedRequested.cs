using Coworkee.Domain;

namespace Coworkee.Identity.Setup;

/// <summary>
/// A tenant was set up at run time (setup wizard, new tenant): the host that processes the outbox (the api) runs the <see cref="Application.Setup.SeedTarget.EachTenant"/>
/// seed contributors for it after the commit.
/// </summary>
public sealed record TenantSeedRequested(Guid TenantId) : IDomainEvent;
