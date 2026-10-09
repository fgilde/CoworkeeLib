using Coworkee.Contracts.Files;
using Microsoft.AspNetCore.Components.Forms;
using MudBlazor.Extensions.Core.FileManager;

namespace Coworkee.Client.Blazor.Files;

/// <summary>Feeds MudExFileManager from the files API; node paths are ids, the actions follow the rights in the current folder.</summary>
public sealed class FilesStructureManager(IFilesApi api, string? accept = null) : IMudExFileStructureManager, IMudExFileStructureWriter
{
    private readonly Dictionary<Guid, FolderContentDto> _contents = [];

    /// <summary>The folder the user is in; its rights decide which actions the manager offers.</summary>
    public Guid? CurrentFolderId { get; set; }

    public MudExFileManagerCapabilities Capabilities
    {
        get
        {
            var capabilities = MudExFileManagerCapabilities.Read | MudExFileManagerCapabilities.Download;
            if (_contents.GetValueOrDefault(CurrentFolderId ?? Guid.Empty) is { } content)
            {
                capabilities |= content.CanUpload ? MudExFileManagerCapabilities.CreateDirectory | MudExFileManagerCapabilities.Upload : 0;
                capabilities |= content.CanManage ? MudExFileManagerCapabilities.Rename | MudExFileManagerCapabilities.Move | MudExFileManagerCapabilities.Delete : 0;
            }

            return capabilities;
        }
    }

    public static Guid? IdOf(MudExFileStructureNode? node) => node is null ? null : Guid.Parse(node.FullPath);

    public Task<HashSet<MudExFileStructureNode>> GetRootAsync(CancellationToken ct = default) => LoadAsync(null, ct);

    public Task<HashSet<MudExFileStructureNode>> GetChildrenAsync(MudExFileStructureNode node, CancellationToken ct = default) => LoadAsync(node, ct);

    public Task<Stream> OpenReadAsync(MudExFileStructureNode node, CancellationToken ct = default) =>
        node is { IsDirectory: false } ? api.OpenReadAsync(IdOf(node)!.Value, ct) : throw new InvalidOperationException("Only files have content.");

    public async Task<MudExFileStructureNode> CreateDirectoryAsync(MudExFileStructureNode parent, string name, CancellationToken ct = default) =>
        Folder(await api.CreateFolderAsync(new CreateFolderRequest(IdOf(parent), name), ct), parent);

    public async Task<MudExFileStructureNode> RenameAsync(MudExFileStructureNode node, string newName, CancellationToken ct = default) => node.IsDirectory
        ? Folder(await api.RenameFolderAsync(IdOf(node)!.Value, newName, ct), node.Parent)
        : File(await api.RenameFileAsync(IdOf(node)!.Value, newName, ct), node.Parent);

    public Task MoveAsync(IReadOnlyCollection<MudExFileStructureNode> nodes, MudExFileStructureNode target, CancellationToken ct = default) =>
        api.MoveAsync(new MoveRequest(Ids(nodes, false), Ids(nodes, true), IdOf(target)), ct);

    public Task DeleteAsync(IReadOnlyCollection<MudExFileStructureNode> nodes, CancellationToken ct = default) =>
        api.DeleteAsync(new FileSelectionRequest(Ids(nodes, false), Ids(nodes, true)), ct);

    public async Task<MudExFileStructureNode> UploadAsync(MudExFileStructureNode target, IBrowserFile file, CancellationToken ct = default)
    {
        await using var content = file.OpenReadStream(long.MaxValue, ct);
        return File(await api.UploadAsync(IdOf(target), file.Name, file.ContentType, content, ct), target);
    }

    private async Task<HashSet<MudExFileStructureNode>> LoadAsync(MudExFileStructureNode? parent, CancellationToken ct)
    {
        var content = await api.GetContentAsync(IdOf(parent), ct);
        _contents[IdOf(parent) ?? Guid.Empty] = content;
        return
        [
            .. content.Folders.Select(f => Folder(f, parent)),
            .. content.Files.Where(f => FileAccept.Matches(accept, f.Name, f.ContentType)).Select(f => File(f, parent)),
        ];
    }

    private static List<Guid> Ids(IEnumerable<MudExFileStructureNode> nodes, bool directories) =>
        nodes.Where(n => n.IsDirectory == directories).Select(n => IdOf(n)!.Value).ToList();

    private MudExFileStructureNode Folder(FolderDto folder, MudExFileStructureNode? parent) => new()
    {
        Name = folder.Name,
        FullPath = folder.Id.ToString(),
        IsDirectory = true,
        LastModified = folder.CreatedAt,
        Handle = folder,
        Parent = parent!,
        LoadChildrenFunc = (node, token) => GetChildrenAsync(node, token),
    };

    private static MudExFileStructureNode File(StoredFileDto file, MudExFileStructureNode? parent) => new()
    {
        Name = file.Name,
        FullPath = file.Id.ToString(),
        Size = file.Size,
        LastModified = file.CreatedAt,
        ContentType = file.ContentType,
        Handle = file,
        Parent = parent!,
    };
}
