namespace Coworkee.Infrastructure.Auditing;

public sealed class AuditChange
{
    public required string Property { get; init; }

    public string? OldValue { get; init; }

    public string? NewValue { get; init; }
}
