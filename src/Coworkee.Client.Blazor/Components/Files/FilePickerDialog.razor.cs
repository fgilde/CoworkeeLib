using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Files;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Files;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Extensions.Core.FileManager;

namespace Coworkee.Client.Blazor.Components.Files;

/// <summary>Browses the files to choose one or more, uploads included; closes with the chosen <see cref="StoredFileDto"/>s.</summary>
public partial class FilePickerDialog
{
    private FilesStructureManager _manager = null!;
    private List<StoredFileDto> _selected = [];

    [Inject] private IFilesApi Api { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [CascadingParameter] private IMudDialogInstance Dialog { get; set; } = null!;

    [Parameter] public bool Multiple { get; set; }

    [Parameter] public string? Accept { get; set; }

    public static async Task<IReadOnlyList<StoredFileDto>?> ShowAsync(IDialogService dialogs, bool multiple, string? accept)
    {
        var parameters = new DialogParameters<FilePickerDialog> { { d => d.Multiple, multiple }, { d => d.Accept, accept } };
        var dialog = await dialogs.ShowSideSheetAsync<FilePickerDialog>(string.Empty, parameters, options => options.MaxWidth = MaxWidth.Large);
        return await dialog.Result is { Canceled: false, Data: IReadOnlyList<StoredFileDto> files } ? files : null;
    }

    protected override void OnInitialized() => _manager = new FilesStructureManager(Api, Accept);

    private void Select(IReadOnlyCollection<MudExFileStructureNode> nodes) => _selected = nodes.Select(n => n.Handle).OfType<StoredFileDto>().ToList();

    private void Enter(MudExFileStructureNode? directory) => _manager.CurrentFolderId = FilesStructureManager.IdOf(directory);

    private void Open(MudExFileStructureNode node)
    {
        if (node.Handle is StoredFileDto file)
        {
            Dialog.Close(DialogResult.Ok<IReadOnlyList<StoredFileDto>>([file]));
        }
    }

    private void Choose() => Dialog.Close(DialogResult.Ok<IReadOnlyList<StoredFileDto>>(_selected));

    private void Cancel() => Dialog.Cancel();
}
