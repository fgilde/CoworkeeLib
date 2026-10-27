namespace Coworkee.Contracts.Files;

public static class FilePermissions
{
    public const string GroupName = "Files";
    public const string View = "Files.View";
    public const string Upload = "Files.Upload";
    public const string Manage = "Files.Manage";

    /// <summary>Opens the registration documents of all users; global file grants do not reach them.</summary>
    public const string ViewRegistrations = "Files.Registrations.View";

    /// <summary>Resource type of folders for per-folder grants; a grant on a folder covers its subfolders.</summary>
    public const string FolderResource = "FileFolder";
}

public sealed record FolderDto(Guid Id, Guid? ParentId, string Name, DateTimeOffset CreatedAt, Guid? CreatedBy);

/// <summary>A stored file; <see cref="IsPublic"/> files are served to anyone under <see cref="FileLinks.Public"/>.</summary>
public sealed record StoredFileDto(Guid Id, Guid? FolderId, string Name, string ContentType, long Size, DateTimeOffset CreatedAt, Guid? CreatedBy, bool IsPublic = false);

public sealed record FilePublicRequest(bool IsPublic);

public static class FileLinks
{
    /// <summary>The anonymous address of a public file, relative to the API (or the web app that forwards /api); ends with the file name so its type shows.</summary>
    public static string Public(Guid id, string name) => $"api/v1/files/public/{id}/{Uri.EscapeDataString(name)}";

    /// <summary>The anonymous address of a file behind a short-lived token, relative to the API; <paramref name="download"/> asks the browser to save it.</summary>
    public static string Token(string token, string name, bool download = false) =>
        $"api/v1/files/token/{Uri.EscapeDataString(token)}/{Uri.EscapeDataString(name)}" + (download ? "?download=true" : string.Empty);
}

/// <summary>A short-lived anonymous link: <paramref name="Url"/> (relative to the API) opens the file without sign-in until <paramref name="ExpiresAt"/>.</summary>
public sealed record FileTokenDto(string Token, string Url, DateTimeOffset ExpiresAt);

/// <summary>A folder's direct children and what the current user may do in it; the root has no folder id.</summary>
public sealed record FolderContentDto(IReadOnlyList<FolderDto> Folders, IReadOnlyList<StoredFileDto> Files, bool CanUpload, bool CanManage);

public sealed record CreateFolderRequest(Guid? ParentId, string Name);

public sealed record RenameRequest(string Name);

public sealed record FileSelectionRequest(IReadOnlyList<Guid> FileIds, IReadOnlyList<Guid> FolderIds);

public sealed record MoveRequest(IReadOnlyList<Guid> FileIds, IReadOnlyList<Guid> FolderIds, Guid? TargetFolderId);
