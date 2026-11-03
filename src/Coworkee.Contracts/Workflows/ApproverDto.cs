namespace Coworkee.Contracts.Workflows;

public sealed record ApproverDto(Guid UserId, bool? Approved, string? Comment, DateTimeOffset? DecidedAt, bool Escalation);
