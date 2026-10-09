using Coworkee.Contracts.Files;
using Coworkee.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Files;

[Realtime(FilePermissions.View)]
public sealed class FileFolder : AuditedAggregateRoot, IMultiTenant, ISoftDelete
{
    public required string Name { get; set; }

    public Guid? ParentId { get; set; }

    public Guid TenantId { get; set; }

    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public Guid? DeletedBy { get; set; }
}

[Realtime(FilePermissions.View)]
public sealed class StoredFile : AuditedAggregateRoot, IMultiTenant, ISoftDelete
{
    public required string Name { get; set; }

    public Guid? FolderId { get; set; }

    public required string ContentType { get; set; }

    public long Size { get; set; }

    public required string BlobKey { get; set; }

    public Guid TenantId { get; set; }

    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public Guid? DeletedBy { get; set; }
}

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

public static class FileNames
{
    public const int MaxLength = 255;

    /// <summary>The trimmed name, or null when it is empty, too long or contains path separators or control characters.</summary>
    public static string? Clean(string? name)
    {
        var trimmed = name?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxLength || trimmed is "." or ".." || trimmed.Any(c => c is '/' or '\\' || char.IsControl(c))
            ? null
            : trimmed;
    }
}
