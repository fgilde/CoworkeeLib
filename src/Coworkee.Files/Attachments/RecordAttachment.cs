using Coworkee.Contracts.Attachments;
using Coworkee.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Files.Attachments;

/// <summary>A file of a record in one of its slots; the content lives in the blob storage under <see cref="BlobKey"/>.</summary>
public sealed class RecordAttachment : AuditedEntity, IMultiTenant, IHasRealtimeTopics
{
    public Guid TenantId { get; set; }

    public required string EntityType { get; set; }

    public Guid EntityId { get; set; }

    public required string Slot { get; set; }

    public required string FileName { get; set; }

    public required string ContentType { get; set; }

    public long Size { get; set; }

    public required string BlobKey { get; set; }

    public IEnumerable<string> RealtimeTopics => [AttachmentTopics.Record(EntityType, EntityId)];
}

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
