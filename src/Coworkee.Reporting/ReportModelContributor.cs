using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Reporting;

internal sealed class ReportModelContributor : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Report>(report =>
        {
            report.ToTable("Reports", "cw");
            report.Property(r => r.Name).HasMaxLength(ReportLimits.NameLength);
            report.Property(r => r.Description).HasMaxLength(ReportLimits.DescriptionLength);
            report.Property(r => r.Icon).HasMaxLength(ReportLimits.IconLength);
            report.Property(r => r.Visibility).HasConversion<string>().HasMaxLength(20);
            report.Property(r => r.Definition).HasColumnType("jsonb");
            report.HasIndex(r => new { r.TenantId, r.OwnerId });
        });

        modelBuilder.Entity<ReportFavorite>(favorite =>
        {
            favorite.ToTable("ReportFavorites", "cw");
            favorite.HasKey(f => new { f.UserId, f.ReportId });
            favorite.HasOne<Report>().WithMany().HasForeignKey(f => f.ReportId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}

internal static class ReportLimits
{
    public const int NameLength = 200;
    public const int DescriptionLength = 2000;
    public const int IconLength = 4000;
    public const int Widgets = 60;
    public const int Aggregates = 8;
    public const int TextLength = 20_000;
}
