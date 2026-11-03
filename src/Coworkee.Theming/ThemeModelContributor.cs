using Coworkee.Infrastructure.Persistence;
using Coworkee.Infrastructure.Versioning;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Theming;

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
            theme.Property(t => t.Shadows).HasColumnType("jsonb");
            theme.Property(t => t.Options).HasColumnType("jsonb");
            theme.HasIndex(t => new { t.TenantId, t.Name }).IsUnique().AreNullsDistinct(false);
            // SQL Server and SQLite let null tenants repeat in the index above, so built-in themes get their own; the quoted name works on all three
            theme.HasIndex(t => t.Name).IsUnique().HasFilter("\"TenantId\" IS NULL");
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
