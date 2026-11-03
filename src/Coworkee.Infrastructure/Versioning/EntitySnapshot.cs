using Coworkee.Domain;

namespace Coworkee.Infrastructure.Versioning;

[NotAudited]
public sealed class EntitySnapshot
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid? TenantId { get; init; }

    public required string EntityType { get; init; }

    public required string EntityId { get; init; }

    public int Revision { get; init; }

    public required string Payload { get; init; }

    public bool IsDeleted { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public Guid? CreatedBy { get; init; }
}
