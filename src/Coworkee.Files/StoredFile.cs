using Coworkee.Contracts.Files;
using Coworkee.Domain;

namespace Coworkee.Files;

[Realtime(FilePermissions.View)]
public sealed class StoredFile : AuditedAggregateRoot, IMultiTenant, ISoftDelete
{
    public required string Name { get; set; }

    public Guid? FolderId { get; set; }

    public required string ContentType { get; set; }

    public long Size { get; set; }

    public required string BlobKey { get; set; }

    /// <summary>Served without sign-in, e.g. as the sign-in page's image.</summary>
    public bool IsPublic { get; set; }

    public Guid TenantId { get; set; }

    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public Guid? DeletedBy { get; set; }
}
