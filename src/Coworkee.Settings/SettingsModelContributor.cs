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
    }
}
