using Coworkee.Domain;
using Nextended.Core.Facets;

namespace Coworkee.Infrastructure.Auditing;

public enum AuditAction
{
    Created,
    Updated,
    Deleted,
    Restored,
}

[NotAudited]
public sealed class AuditEntry
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid? TenantId { get; init; }

    [ProvideFacet(Label = "Entity")]
    public required string EntityType { get; init; }

    public required string EntityId { get; init; }

    [ProvideFacet(Label = "Action")]
    public AuditAction Action { get; init; }

    public Guid? ActorId { get; init; }

    public string? CorrelationId { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    public List<AuditChange> Changes { get; init; } = [];
}

public sealed class AuditChange
{
    public required string Property { get; init; }

    public string? OldValue { get; init; }

    public string? NewValue { get; init; }
}
