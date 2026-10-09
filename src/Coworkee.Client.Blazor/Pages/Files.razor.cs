using Coworkee.Client.Blazor.Components.Files;
using Coworkee.Client.Blazor.Files;
using Coworkee.Contracts.Files;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Extensions.Core.FileManager;

namespace Coworkee.Client.Blazor.Pages;

public partial class Files
{
    private FilesStructureManager _manager = null!;

    [Inject] private IFilesApi Api { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    protected override void OnInitialized() => _manager = new FilesStructureManager(Api);

    private void Enter(MudExFileStructureNode? directory) => _manager.CurrentFolderId = FilesStructureManager.IdOf(directory);

    private Task PreviewAsync(MudExFileStructureNode node) => node.Handle is StoredFileDto file ? FilePreviewDialog.ShowAsync(Dialogs, file) : Task.CompletedTask;
}
