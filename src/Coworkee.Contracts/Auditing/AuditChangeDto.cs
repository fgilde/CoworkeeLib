namespace Coworkee.Contracts.Auditing;

public sealed record AuditChangeDto(string Property, string? OldValue, string? NewValue);
