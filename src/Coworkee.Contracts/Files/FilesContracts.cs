namespace Coworkee.Contracts.Files;

public static class FilePermissions
{
    public const string GroupName = "Files";
    public const string View = "Files.View";
    public const string Upload = "Files.Upload";
    public const string Manage = "Files.Manage";

    /// <summary>Resource type of folders for per-folder grants; a grant on a folder covers its subfolders.</summary>
    public const string FolderResource = "FileFolder";
}

public sealed record FolderDto(Guid Id, Guid? ParentId, string Name, DateTimeOffset CreatedAt, Guid? CreatedBy);

public sealed record StoredFileDto(Guid Id, Guid? FolderId, string Name, string ContentType, long Size, DateTimeOffset CreatedAt, Guid? CreatedBy);

/// <summary>A folder's direct children and what the current user may do in it; the root has no folder id.</summary>
public sealed record FolderContentDto(IReadOnlyList<FolderDto> Folders, IReadOnlyList<StoredFileDto> Files, bool CanUpload, bool CanManage);

public sealed record CreateFolderRequest(Guid? ParentId, string Name);

public sealed record RenameRequest(string Name);

public sealed record FileSelectionRequest(IReadOnlyList<Guid> FileIds, IReadOnlyList<Guid> FolderIds);

public sealed record MoveRequest(IReadOnlyList<Guid> FileIds, IReadOnlyList<Guid> FolderIds, Guid? TargetFolderId);
