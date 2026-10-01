using Coworkee.Infrastructure.Auditing;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.AuthServer;

internal sealed class AuthModelContributor : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.UseOpenIddict<Guid>();
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(t => t.ClrType.Namespace?.StartsWith("OpenIddict", StringComparison.Ordinal) == true))
        {
            entityType.SetSchema("auth");
            entityType.IsNotAudited();
        }
    }
}
