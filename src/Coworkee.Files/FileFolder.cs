using Coworkee.Contracts.Files;
using Coworkee.Domain;

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
