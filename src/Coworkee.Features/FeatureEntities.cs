using System.Text.Json;
using Coworkee.Contracts.Features;
using Coworkee.Domain;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coworkee.Features;

/// <summary>A named set of feature values that tenants get assigned.</summary>
public sealed class Edition : AuditedAggregateRoot, IHasRealtimeTopics
{
    public required string Name { get; set; }

    public string? Description { get; set; }

    public Dictionary<string, string> Values { get; set; } = new(StringComparer.Ordinal);

    public IEnumerable<string> RealtimeTopics => [FeatureTopics.Changed];
}

/// <summary>The edition of a tenant and the values it overrides.</summary>
public sealed class TenantFeatureSet : IHasRealtimeTopics
{
    public Guid TenantId { get; set; }

    public Guid? EditionId { get; set; }

    public Dictionary<string, string> Overrides { get; set; } = new(StringComparer.Ordinal);

    public IEnumerable<string> RealtimeTopics => [FeatureTopics.Changed];
}

internal sealed class FeatureModelContributor : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Edition>(edition =>
        {
            edition.ToTable("Editions", "cw");
            edition.Property(e => e.Name).HasMaxLength(100);
            edition.Property(e => e.Description).HasMaxLength(500);
            edition.HasIndex(e => e.Name).IsUnique();
            Json(edition.Property(e => e.Values));
        });

        modelBuilder.Entity<TenantFeatureSet>(set =>
        {
            set.ToTable("TenantFeatures", "cw");
            set.HasKey(s => s.TenantId);
            set.HasOne<Tenant>().WithOne().HasForeignKey<TenantFeatureSet>(s => s.TenantId).OnDelete(DeleteBehavior.Cascade);
            set.HasOne<Edition>().WithMany().HasForeignKey(s => s.EditionId).OnDelete(DeleteBehavior.SetNull);
            Json(set.Property(s => s.Overrides));
        });
    }

    private static void Json(PropertyBuilder<Dictionary<string, string>> property) =>
        property.HasColumnType("jsonb").HasConversion(
            v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
            v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, JsonSerializerOptions.Default) ?? new Dictionary<string, string>(),
            new ValueComparer<Dictionary<string, string>>(
                (a, b) => a!.Count == b!.Count && !a.Except(b).Any(),
                v => v.Aggregate(0, (hash, pair) => hash ^ HashCode.Combine(pair.Key, pair.Value)),
                v => new Dictionary<string, string>(v, StringComparer.Ordinal)));
}
