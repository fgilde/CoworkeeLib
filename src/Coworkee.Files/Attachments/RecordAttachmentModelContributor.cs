using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Files.Attachments;

internal sealed class RecordAttachmentModelContributor : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder) => modelBuilder.Entity<RecordAttachment>(attachment =>
    {
        attachment.ToTable("RecordAttachments", "cw");
        attachment.Property(a => a.EntityType).HasMaxLength(100);
        attachment.Property(a => a.Slot).HasMaxLength(50);
        attachment.Property(a => a.FileName).HasMaxLength(FileNames.MaxLength);
        attachment.Property(a => a.ContentType).HasMaxLength(200);
        attachment.Property(a => a.BlobKey).HasMaxLength(512);
        attachment.HasIndex(a => new { a.TenantId, a.EntityType, a.EntityId, a.Slot });
    });
}
