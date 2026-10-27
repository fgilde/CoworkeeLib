using Coworkee.Contracts.Files;

namespace Coworkee.Client.Blazor.Files;

public interface IFilesApi
{
    /// <summary>The direct children of a folder, or of the root when <paramref name="folderId"/> is null.</summary>
    Task<FolderContentDto> GetContentAsync(Guid? folderId, CancellationToken cancellationToken = default);

    Task<StoredFileDto> GetFileAsync(Guid id, CancellationToken cancellationToken = default);

    Task<FolderDto> CreateFolderAsync(CreateFolderRequest request, CancellationToken cancellationToken = default);

    Task<FolderDto> RenameFolderAsync(Guid id, string name, CancellationToken cancellationToken = default);

    Task<StoredFileDto> RenameFileAsync(Guid id, string name, CancellationToken cancellationToken = default);

    /// <summary>Public files are served to anyone under <see cref="FileLinks.Public"/>.</summary>
    Task<StoredFileDto> SetPublicAsync(Guid id, bool isPublic, CancellationToken cancellationToken = default);

    Task MoveAsync(MoveRequest request, CancellationToken cancellationToken = default);

    Task DeleteAsync(FileSelectionRequest request, CancellationToken cancellationToken = default);

    /// <summary>Sends <paramref name="content"/> as the request body, so the server streams it into the storage.</summary>
    Task<StoredFileDto> UploadAsync(Guid? folderId, string name, string? contentType, Stream content, CancellationToken cancellationToken = default);

    /// <summary>The file's content; the caller disposes the stream.</summary>
    Task<Stream> OpenReadAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>A link that opens the file without sign-in for five minutes (<see cref="FileTokenDto.Url"/>, relative to the API).</summary>
    Task<FileTokenDto> CreateTokenAsync(Guid id, CancellationToken cancellationToken = default);
}
