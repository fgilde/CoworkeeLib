using Coworkee.Infrastructure.Auditing;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.ApiKeys;

internal sealed class ApiKeyModel : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<ApiKey>(key =>
        {
            key.ToTable("ApiKeys", "cw");
            key.Property(k => k.Name).HasMaxLength(ApiKeyLimits.Name);
            key.Property(k => k.Description).HasMaxLength(ApiKeyLimits.Description);
            key.Property(k => k.Prefix).HasMaxLength(20);
            key.Property(k => k.SecretHash).HasMaxLength(64).IsNotAudited();
            key.Property(k => k.LastUsedIp).HasMaxLength(64);
            key.HasIndex(k => k.SecretHash).IsUnique();
            key.HasIndex(k => new { k.TenantId, k.UserId });
        });
}

internal static class ApiKeyLimits
{
    public const int Name = 100;

    public const int Description = 500;
}
