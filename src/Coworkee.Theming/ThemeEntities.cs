using Coworkee.Domain;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Infrastructure.Versioning;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Theming;

public sealed class ThemeDefinition : AuditedAggregateRoot, IVersioned
{
    public required string Name { get; set; }

    public Guid? TenantId { get; set; }

    public bool IsDefault { get; set; }

    public required string PaletteLight { get; set; }

    public required string PaletteDark { get; set; }

    public string? Typography { get; set; }

    public string? LayoutProperties { get; set; }

    public string? LogoSvg { get; set; }

    public string? CustomCss { get; set; }

    public int Revision { get; set; }
}

public sealed class TenantTheme
{
    public Guid TenantId { get; set; }

    public Guid ThemeId { get; set; }
}

internal sealed class ThemeModelContributor : IModelContributor, IVersionedTypeContributor
{
    public void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ThemeDefinition>(theme =>
        {
            theme.ToTable("Themes", "cw");
            theme.Property(t => t.Name).HasMaxLength(100);
            theme.Property(t => t.PaletteLight).HasColumnType("jsonb");
            theme.Property(t => t.PaletteDark).HasColumnType("jsonb");
            theme.Property(t => t.Typography).HasColumnType("jsonb");
            theme.Property(t => t.LayoutProperties).HasColumnType("jsonb");
            theme.HasIndex(t => new { t.TenantId, t.Name }).IsUnique().AreNullsDistinct(false);
        });

        modelBuilder.Entity<TenantTheme>(mapping =>
        {
            mapping.ToTable("TenantThemes", "cw");
            mapping.HasKey(m => m.TenantId);
            mapping.HasOne<ThemeDefinition>().WithMany().HasForeignKey(m => m.ThemeId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    public void Define(VersionedTypeContext context) => context.Add<ThemeDefinition>("Theme", Contracts.Theming.ThemePermissions.Manage);
}
