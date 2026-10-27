using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Files;
using Coworkee.Client.Blazor.Files;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Files;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;
using MudBlazor.Extensions.Core.FileManager;

namespace Coworkee.Client.Blazor.Pages;

public partial class Files
{
    private FilesStructureManager _manager = null!;
    private MudExFileStructureNode? _node;

    [Inject] private IFilesApi Api { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private IJSRuntime JS { get; set; } = null!;

    [Inject] private NavigationManager Navigation { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    /// <summary>A file to preview on arrival, e.g. a hit of the global search.</summary>
    [SupplyParameterFromQuery(Name = "file")] public Guid? File { get; set; }

    /// <summary>The one file selected; sharing acts on it.</summary>
    private StoredFileDto? Selected => _node?.Handle as StoredFileDto;

    protected override void OnInitialized() => _manager = new FilesStructureManager(Api);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && File is { } id)
        {
            try
            {
                await FilePreviewDialog.ShowAsync(Dialogs, await Api.GetFileAsync(id));
            }
            catch (ApiException exception)
            {
                Snackbar.Add(SnackbarApiExtensions.Describe(exception), Severity.Warning);
            }
        }
    }

    private void Enter(MudExFileStructureNode? directory) => _manager.CurrentFolderId = FilesStructureManager.IdOf(directory);

    private void Select(IReadOnlyCollection<MudExFileStructureNode> nodes) => _node = nodes.Count == 1 && nodes.First() is { IsDirectory: false } node ? node : null;

    private async Task TogglePublicAsync()
    {
        if (_node is { Handle: StoredFileDto file } node && await FilePublishing.SetPublicAsync(Dialogs, Api, Snackbar, L, file, !file.IsPublic) is { } changed)
        {
            node.Handle = changed;
        }
    }

    private Task CopyTemporaryLinkAsync() => Selected is { } file ? FilePublishing.CopyTemporaryLinkAsync(JS, Api, Snackbar, L, Navigation, file) : Task.CompletedTask;

    private Task CopyLinkAsync() => Selected is { IsPublic: true } file ? FilePublishing.CopyPublicLinkAsync(JS, Snackbar, L, Navigation, file) : Task.CompletedTask;

    private Task PreviewAsync(MudExFileStructureNode node) => node.Handle is StoredFileDto file ? FilePreviewDialog.ShowAsync(Dialogs, file) : Task.CompletedTask;
}
