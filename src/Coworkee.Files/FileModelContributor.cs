using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Files;

internal sealed class FileModelContributor : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FileFolder>(folder =>
        {
            folder.ToTable("FileFolders", "cw");
            folder.Property(f => f.Name).HasMaxLength(FileNames.MaxLength);
            folder.HasIndex(f => new { f.TenantId, f.ParentId });
            folder.HasOne<FileFolder>().WithMany().HasForeignKey(f => f.ParentId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StoredFile>(file =>
        {
            file.ToTable("StoredFiles", "cw");
            file.Property(f => f.Name).HasMaxLength(FileNames.MaxLength);
            file.Property(f => f.ContentType).HasMaxLength(200);
            file.Property(f => f.BlobKey).HasMaxLength(512);
            file.HasIndex(f => new { f.TenantId, f.FolderId });
            file.HasOne<FileFolder>().WithMany().HasForeignKey(f => f.FolderId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
