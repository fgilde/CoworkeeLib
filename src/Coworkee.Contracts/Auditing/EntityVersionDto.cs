namespace Coworkee.Contracts.Auditing;

public sealed record EntityVersionDto(int Revision, DateTimeOffset CreatedAt, Guid? CreatedBy, string? CreatedByName, bool IsDeleted);
