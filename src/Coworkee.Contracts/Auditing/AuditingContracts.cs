using System.Text.Json;

namespace Coworkee.Contracts.Auditing;

public static class AuditPermissions
{
    public const string GroupName = "Audit";
    public const string View = "Audit.View";
}

public sealed record AuditChangeDto(string Property, string? OldValue, string? NewValue);

public sealed record AuditEntryDto(
    Guid Id, string EntityType, string EntityId, string Action, Guid? ActorId, string? ActorName, DateTimeOffset OccurredAt, string? CorrelationId, IReadOnlyList<AuditChangeDto> Changes);

public sealed record AuditQuery(
    string? EntityType = null, string? EntityId = null, Guid? ActorId = null, DateTimeOffset? From = null, DateTimeOffset? To = null, int Page = 1, int PageSize = 25);

public sealed record EntityVersionDto(int Revision, DateTimeOffset CreatedAt, Guid? CreatedBy, string? CreatedByName, bool IsDeleted);

public sealed record EntityVersionDetailDto(int Revision, JsonElement Payload, bool IsDeleted);
