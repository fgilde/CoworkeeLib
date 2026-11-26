using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.DataFilters;
using Coworkee.Contracts.Query;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.DataFilters;

/// <summary>"Same data as {set} via {property}": the rows of a set are visible only when the row of the other set they point to passes that set's filters. Only for who manages System filters.</summary>
public partial class DataFilterLinkEditor
{
    private DataFilterLinkDto? _link;
    private IReadOnlyList<string> _sets = [];
    private IReadOnlyList<string> _keys = [];
    private string? _target;
    private string? _property;
    private string? _problem;
    private bool _allowed;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private PermissionStore Permissions { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public string EntitySet { get; set; } = string.Empty;

    protected override async Task OnInitializedAsync()
    {
        _allowed = await Permissions.HasAsync(DataFilterPermissions.ManageSystem);
        if (_allowed)
        {
            _sets = await Api.GetDataFilterSetsAsync();
            // the link joins by the key of the target: only own Guid properties can hold one
            _keys = [.. (await Api.GetDataFilterFieldsAsync(EntitySet)).Where(f => f.Type == QueryFieldType.Guid && !f.Name.Contains('.')).Select(f => f.Name)];
            await LoadAsync();
        }
    }

    private Task ReloadAsync() => InvokeAsync(async () =>
    {
        await LoadAsync();
        StateHasChanged();
    });

    private async Task LoadAsync()
    {
        await Snackbar.RunAsync(async () => _link = (await Api.GetDataFilterLinksAsync()).FirstOrDefault(l => string.Equals(l.EntitySet, EntitySet, StringComparison.OrdinalIgnoreCase)));
        (_target, _property) = (_link?.TargetSet, _link?.Property);
    }

    private async Task SaveAsync()
    {
        try
        {
            await Api.SaveDataFilterLinkAsync(new SaveDataFilterLinkRequest(EntitySet, _property!, _target!));
            _problem = null;
            await LoadAsync();
        }
        catch (ApiException exception)
        {
            _problem = exception.Message;
        }
    }

    private async Task RemoveAsync()
    {
        if (_link is not null && await Snackbar.RunAsync(() => Api.DeleteDataFilterLinkAsync(_link.Id)))
        {
            await LoadAsync();
        }
    }
}
