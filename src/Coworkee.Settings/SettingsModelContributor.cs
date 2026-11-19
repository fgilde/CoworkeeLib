using Coworkee.Infrastructure.Auditing;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Settings;

internal sealed class SettingsModelContributor : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SettingValue>(setting =>
        {
            setting.ToTable("SettingValues", "cw");
            setting.Property(s => s.Name).HasMaxLength(200);
            setting.Property(s => s.Scope).HasConversion<string>().HasMaxLength(20);
            setting.Property(s => s.Value).IsSensitive();
            setting.HasIndex(s => new { s.Name, s.Scope, s.ScopeKey }).IsUnique().AreNullsDistinct(false);
        });
        modelBuilder.Entity<ClientValue>(value =>
        {
            value.ToTable("ClientValues", "cw");
            value.Property(v => v.Key).HasMaxLength(ClientValue.KeyLength);
            // one own value per user and key, and one global value (no user) per organisation and key: no filter, so SQL Server treats null users as
            // equal (it would add "UserId IS NOT NULL"), and PostgreSQL likewise with NULLS NOT DISTINCT
            value.HasIndex(v => new { v.TenantId, v.Key, v.UserId }).IsUnique().HasFilter(null).AreNullsDistinct(false);
        });
    }
}
