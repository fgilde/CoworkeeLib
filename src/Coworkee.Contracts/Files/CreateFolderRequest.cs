namespace Coworkee.Contracts.Files;

public sealed record CreateFolderRequest(Guid? ParentId, string Name);
