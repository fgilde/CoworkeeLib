using Coworkee.Contracts.Settings;
using Coworkee.Domain;
using Coworkee.Infrastructure.Auditing;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Settings;

public sealed class SettingValue : AuditedEntity
{
    public required string Name { get; set; }

    public SettingScope Scope { get; set; }

    public Guid? ScopeKey { get; set; }

    public string? Value { get; set; }
}

[NotAudited]
public sealed class DataProtectionKey
{
    public int Id { get; set; }

    public string? FriendlyName { get; set; }

    public required string Xml { get; set; }
}

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

        modelBuilder.Entity<DataProtectionKey>(key =>
        {
            key.ToTable("DataProtectionKeys", "cw");
            key.Property(k => k.FriendlyName).HasMaxLength(200);
        });
    }
}
