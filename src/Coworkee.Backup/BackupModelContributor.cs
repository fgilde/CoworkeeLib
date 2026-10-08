using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Backup;

internal sealed class BackupModelContributor : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<DatabaseBackup>(backup =>
        {
            backup.ToTable("DatabaseBackups", "cw");
            backup.Property(b => b.Name).HasMaxLength(200);
            backup.Property(b => b.BlobKey).HasMaxLength(500);
            backup.Property(b => b.Migration).HasMaxLength(300);
        });
}
