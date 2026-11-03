using Coworkee.Contracts.Files;

namespace Coworkee.Files;

internal static class FileMapping
{
    public static FolderDto ToDto(FileFolder folder) => new(folder.Id, folder.ParentId, folder.Name, folder.CreatedAt, folder.CreatedBy);

    public static StoredFileDto ToDto(StoredFile file) => new(file.Id, file.FolderId, file.Name, file.ContentType, file.Size, file.CreatedAt, file.CreatedBy, file.IsPublic);
}
