using System.Text.Json;

namespace Coworkee.Contracts.Auditing;

public static class AuditPermissions
{
    public const string GroupName = "Audit";
    public const string View = "Audit.View";
}

public sealed record AuditChangeDto(string Property, string? OldValue, string? NewValue);

/// <summary><paramref name="ImpersonatorId"/>: the user who signed in as the actor ("Log in as") and made the change.</summary>
public sealed record AuditEntryDto(
    Guid Id, string EntityType, string EntityId, string Action, Guid? ActorId, string? ActorName, DateTimeOffset OccurredAt, string? CorrelationId, IReadOnlyList<AuditChangeDto> Changes,
    Guid? ImpersonatorId = null);

/// <summary><paramref name="ImpersonatorId"/>: only changes someone made while signed in as another user ("Log in as").</summary>
public sealed record AuditQuery(
    string? EntityType = null, string? EntityId = null, Guid? ActorId = null, DateTimeOffset? From = null, DateTimeOffset? To = null, int Page = 1, int PageSize = 25,
    Guid? ImpersonatorId = null);

public sealed record EntityVersionDto(int Revision, DateTimeOffset CreatedAt, Guid? CreatedBy, string? CreatedByName, bool IsDeleted);

public sealed record EntityVersionDetailDto(int Revision, JsonElement Payload, bool IsDeleted);
