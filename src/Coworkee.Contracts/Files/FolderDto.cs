namespace Coworkee.Contracts.Files;

public sealed record FolderDto(Guid Id, Guid? ParentId, string Name, DateTimeOffset CreatedAt, Guid? CreatedBy);
