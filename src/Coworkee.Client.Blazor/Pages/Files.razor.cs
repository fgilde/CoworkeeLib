using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Files;
using Coworkee.Client.Blazor.Files;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Files;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;
using MudBlazor.Extensions.Components;
using MudBlazor.Extensions.Core.FileManager;
using Nextended.Core.Types;

namespace Coworkee.Client.Blazor.Pages;

public partial class Files
{
    private static readonly TimeSpan LiveSettle = TimeSpan.FromMilliseconds(250);
    private FilesStructureManager _manager = null!;
    private MudExFileManager _files = null!;
    private MudExFileStructureNode? _directory;
    private MudExFileStructureNode? _node;
    private CancellationTokenSource? _live;

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

    private void Enter(MudExFileStructureNode? directory)
    {
        _directory = directory;
        _manager.CurrentFolderId = FilesStructureManager.IdOf(directory);
    }

    /// <summary>
    /// A file or folder changed in any session: the drive reloads once per burst (a drop of twenty files sends twenty events) and opens the folder
    /// the user was in again, as far as it still exists.
    /// </summary>
    private async Task LiveChangedAsync()
    {
        _live?.Cancel();
        var live = _live = new CancellationTokenSource();
        try
        {
            await Task.Delay(LiveSettle, live.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var path = _directory?.Path().Select(n => n.FullPath).ToList() ?? [];
        await _files.RefreshAsync();
        Enter(null);
        _node = null;
        MudExFileStructureNode? folder = null;
        IEnumerable<MudExFileStructureNode> level = _files.RootItems;
        foreach (var id in path)
        {
            if (level.FirstOrDefault(n => n.IsDirectory && n.FullPath == id) is not { } next)
            {
                break;
            }

            folder = next;
            await folder.LoadChildren();
            level = folder.GetLoadedChildren();
        }

        if (folder is not null)
        {
            await _files.NavigateToAsync(folder);
        }
    }

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
