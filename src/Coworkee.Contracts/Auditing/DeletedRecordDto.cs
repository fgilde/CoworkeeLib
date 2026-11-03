namespace Coworkee.Contracts.Auditing;

public sealed record DeletedRecordDto(string EntityId, string? Title, DateTimeOffset DeletedAt, string? DeletedByName);
