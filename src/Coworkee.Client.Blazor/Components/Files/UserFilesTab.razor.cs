using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Files;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Files;

/// <summary>
/// The files a user uploaded (<c>CreatedBy</c>), his registration documents (in a folder below <see cref="FolderDto.RegistrationsRoot"/>) apart;
/// the tab "Files" of the admin user page. The server hides the registration documents from readers without Files.Registrations.View.
/// </summary>
public partial class UserFilesTab
{
    private const string FileFields = "Id,FolderId,Name,ContentType,Size,CreatedAt,CreatedBy,IsPublic";
    private List<StoredFileDto>? _files;
    private HashSet<Guid> _registrationFolders = [];

    [Parameter] public Guid UserId { get; set; }

    [Inject] private IODataClient OData { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;

    protected override Task OnParametersSetAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        // ponytail: the newest 500 files of the user; paging if users keep more
        var files = (await OData.QueryAsync<StoredFileDto>("StoredFiles",
            new ODataQuery { Filter = $"CreatedBy eq {UserId}", OrderBy = "CreatedAt desc", Select = FileFields, Top = 500, Count = false })).Items;
        var folderIds = files.Select(f => f.FolderId).OfType<Guid>().Distinct().ToList();
        HashSet<Guid> registration = [];
        if (folderIds.Count > 0)
        {
            var folders = (await OData.QueryAsync<FolderDto>("FileFolders", new ODataQuery
            {
                Filter = $"(Name eq '{FolderDto.RegistrationsRoot}' and ParentId eq null) or Id in ({string.Join(",", folderIds)})", Select = "Id,Name,ParentId", Count = false,
            })).Items;
            var roots = folders.Where(f => f.ParentId is null && f.Name == FolderDto.RegistrationsRoot).Select(f => f.Id).ToHashSet();
            registration = [.. folders.Where(f => f.ParentId is { } parent && roots.Contains(parent)).Select(f => f.Id)];
        }

        _registrationFolders = registration;
        _files = [.. files];
        StateHasChanged();
    }

    private IEnumerable<(string Title, string TestId, List<StoredFileDto> Files)> Groups()
    {
        var registration = _files!.Where(f => f.FolderId is { } id && _registrationFolders.Contains(id)).ToList();
        var others = _files!.Except(registration).ToList();
        if (registration.Count > 0)
        {
            yield return ("Registration documents", "user-files-registration", registration);
        }

        if (others.Count > 0)
        {
            yield return ("Other files", "user-files-other", others);
        }
    }

    private Task PreviewAsync(StoredFileDto file) => FilePreviewDialog.ShowAsync(Dialogs, file);
}
