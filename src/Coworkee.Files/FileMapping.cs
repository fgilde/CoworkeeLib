using Coworkee.Contracts.Files;
using Coworkee.Core.Results;

namespace Coworkee.Files;

internal static class FileMapping
{
    public static FolderDto ToDto(FileFolder folder) => new(folder.Id, folder.ParentId, folder.Name, folder.CreatedAt, folder.CreatedBy);

    public static StoredFileDto ToDto(StoredFile file) => new(file.Id, file.FolderId, file.Name, file.ContentType, file.Size, file.CreatedAt, file.CreatedBy);
}

internal static class FileErrors
{
    public static readonly Error FolderNotFound = Error.NotFound("files.folder_not_found", "The folder does not exist.");
    public static readonly Error FileNotFound = Error.NotFound("files.file_not_found", "The file does not exist.");
    public static readonly Error Forbidden = Error.Forbidden("auth.forbidden", "You may not do this in this folder.");
    public static readonly Error InvalidName = Error.Validation("Name", "Enter a name without slashes, at most 255 characters.");
    public static readonly Error NoTenant = Error.Validation("Tenant", "Files belong to an organisation.");
    public static readonly Error IntoItself = Error.Validation("TargetFolderId", "A folder cannot be moved into itself.");
}
