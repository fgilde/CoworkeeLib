using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.OData.Transfer;

internal sealed class DataTransferModelContributor : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DataTransferJob>(job =>
        {
            job.ToTable("DataTransferJobs", "cw");
            job.Property(j => j.EntitySet).HasMaxLength(200);
            job.Property(j => j.Kind).HasConversion<string>().HasMaxLength(20);
            job.Property(j => j.Status).HasConversion<string>().HasMaxLength(20);
            job.Property(j => j.FileName).HasMaxLength(260);
            job.Property(j => j.ResultFileName).HasMaxLength(260);
            job.Property(j => j.ResultContentType).HasMaxLength(100);
            job.Property(j => j.Message).HasMaxLength(2000);
            job.HasIndex(j => new { j.TenantId, j.UserId, j.CreatedAt });
        });
        modelBuilder.Entity<ImportProfile>(profile =>
        {
            profile.ToTable("ImportProfiles", "cw");
            profile.Property(p => p.EntitySet).HasMaxLength(200);
            profile.Property(p => p.Name).HasMaxLength(200);
            profile.Property(p => p.Mode).HasConversion<string>().HasMaxLength(20);
            profile.Property(p => p.KeyField).HasMaxLength(200);
            profile.Property(p => p.Culture).HasMaxLength(20);
            profile.HasIndex(p => new { p.TenantId, p.EntitySet, p.Name }).IsUnique();
        });
        modelBuilder.Entity<ExportSchedule>(schedule =>
        {
            schedule.ToTable("ExportSchedules", "cw");
            schedule.Ignore(s => s.Schedule);
            schedule.Property(s => s.EntitySet).HasMaxLength(200);
            schedule.Property(s => s.Name).HasMaxLength(200);
            schedule.Property(s => s.Query).HasMaxLength(4000);
            schedule.Property(s => s.FileName).HasMaxLength(260);
            schedule.Property(s => s.Frequency).HasConversion<string>().HasMaxLength(20);
            schedule.Property(s => s.Week).HasConversion<string>().HasMaxLength(10);
            schedule.Property(s => s.TimeZone).HasMaxLength(100);
            schedule.Property(s => s.LastError).HasMaxLength(2000);
            schedule.HasIndex(s => s.NextRunAt);
            schedule.HasIndex(s => new { s.TenantId, s.UserId });
        });
    }
}
