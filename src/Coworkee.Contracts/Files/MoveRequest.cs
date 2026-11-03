namespace Coworkee.Contracts.Files;

public sealed record MoveRequest(IReadOnlyList<Guid> FileIds, IReadOnlyList<Guid> FolderIds, Guid? TargetFolderId);
