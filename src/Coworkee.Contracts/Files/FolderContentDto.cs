namespace Coworkee.Contracts.Files;

/// <summary>A folder's direct children and what the current user may do in it; the root has no folder id.</summary>
public sealed record FolderContentDto(IReadOnlyList<FolderDto> Folders, IReadOnlyList<StoredFileDto> Files, bool CanUpload, bool CanManage);
