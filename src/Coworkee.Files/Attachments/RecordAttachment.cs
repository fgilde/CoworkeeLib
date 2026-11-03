using Coworkee.Contracts.Attachments;
using Coworkee.Domain;

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
