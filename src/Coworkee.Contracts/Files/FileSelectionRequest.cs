namespace Coworkee.Contracts.Files;

public sealed record FileSelectionRequest(IReadOnlyList<Guid> FileIds, IReadOnlyList<Guid> FolderIds);
