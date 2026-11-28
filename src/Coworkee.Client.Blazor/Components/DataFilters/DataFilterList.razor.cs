using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.DataFilters;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.DataFilters;

/// <summary>
/// The data filters of one layer: of a group or role across every set (<see cref="EntitySet"/> unset: each filter and a picker for the next set), or
/// of one set (the Data types page: System or Tenant). Shows nothing to users without the layer's manage permission (System: nor outside the system
/// organisation) and loads again when
/// the filters change anywhere (<see cref="DataFilterTopics"/>). <see cref="ReadOnly"/> shows them without editing; a list the server refuses (403: role filters
/// for a group-scoped administrator) shows a hint instead of an error.
/// </summary>
public partial class DataFilterList
{
    private IReadOnlyList<DataFilterDto> _filters = [];
    private IReadOnlyList<string> _sets = [];
    private string? _adding;
    private bool _allowed;
    private bool _loaded;
    private bool _refused;
    private Guid? _key;
    private bool _readOnly;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private PermissionStore Permissions { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [CascadingParameter] private Task<AuthenticationState>? AuthenticationState { get; set; }

    [Parameter, EditorRequired] public DataFilterLayer Layer { get; set; }

    /// <summary>The group or role; null for System and Tenant.</summary>
    [Parameter] public Guid? LayerKey { get; set; }

    /// <summary>Limits the list to one set and always offers its editor.</summary>
    [Parameter] public string? EntitySet { get; set; }

    /// <summary>Shows the filters without editing them, under the hint that an administrator above set them (a scoped administrator's own top group).</summary>
    [Parameter] public bool ReadOnly { get; set; }

    /// <summary>A heading (English; translated here).</summary>
    [Parameter] public string? Title { get; set; }

    /// <summary>The permission that manages the filters of <paramref name="layer"/>.</summary>
    public static string PermissionOf(DataFilterLayer layer) => layer switch
    {
        DataFilterLayer.System => DataFilterPermissions.ManageSystem,
        DataFilterLayer.Tenant => DataFilterPermissions.ManageTenant,
        DataFilterLayer.Group => DataFilterPermissions.ManageGroups,
        _ => DataFilterPermissions.ManageRoles,
    };

    private IEnumerable<string> Unfiltered => _sets.Where(s => !_filters.Any(f => string.Equals(f.EntitySet, s, StringComparison.OrdinalIgnoreCase)));

    protected override async Task OnParametersSetAsync()
    {
        if (!_loaded)
        {
            // System filters are written in the system organisation only; every organisation's admin holds the permission
            _allowed = await Permissions.HasAsync(PermissionOf(Layer))
                && (Layer != DataFilterLayer.System || AuthenticationState is null || !SystemTenant.IsOutside((await AuthenticationState).User));
            _loaded = true;
            (_key, _readOnly) = (LayerKey, ReadOnly);
            if (_allowed)
            {
                await LoadAsync();
            }
        }
        else if (_allowed && (_key != LayerKey || _readOnly != ReadOnly))
        {
            // the group or role of the tab changed, or it became editable (the picker needs the sets)
            (_key, _readOnly) = (LayerKey, ReadOnly);
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
        _adding = null;
        _refused = false;
        try
        {
            _filters = await Api.GetDataFiltersAsync(EntitySet, Layer, LayerKey);
            _sets = EntitySet is null && !ReadOnly ? await Api.GetDataFilterSetsAsync() : [];
        }
        catch (ApiException exception) when (exception.Status == 403)
        {
            // role filters bind every group: a group-scoped administrator holds the permission but not the reach
            (_refused, _filters, _sets) = (true, [], []);
        }
        catch (ApiException exception)
        {
            Snackbar.Add(SnackbarApiExtensions.Describe(exception), Severity.Error);
        }
    }
}
