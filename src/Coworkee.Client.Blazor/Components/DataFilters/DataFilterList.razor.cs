using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.DataFilters;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.DataFilters;

/// <summary>
/// The data filters of one layer: of a group or role across every set (<see cref="EntitySet"/> unset: each filter and a picker for the next set), or
/// of one set (the Data types page: System or Tenant). Shows nothing to users without the layer's manage permission and loads again when
/// the filters change anywhere (<see cref="DataFilterTopics"/>).
/// </summary>
public partial class DataFilterList
{
    private IReadOnlyList<DataFilterDto> _filters = [];
    private IReadOnlyList<string> _sets = [];
    private string? _adding;
    private bool _allowed;
    private bool _loaded;
    private Guid? _key;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private PermissionStore Permissions { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public DataFilterLayer Layer { get; set; }

    /// <summary>The group or role; null for System and Tenant.</summary>
    [Parameter] public Guid? LayerKey { get; set; }

    /// <summary>Limits the list to one set and always offers its editor.</summary>
    [Parameter] public string? EntitySet { get; set; }

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
            _allowed = await Permissions.HasAsync(PermissionOf(Layer));
            _loaded = true;
            _key = LayerKey;
            if (_allowed)
            {
                await LoadAsync();
            }
        }
        else if (_allowed && _key != LayerKey)
        {
            // the group or role of the tab changed
            _key = LayerKey;
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
        await Snackbar.RunAsync(async () =>
        {
            _filters = await Api.GetDataFiltersAsync(EntitySet, Layer, LayerKey);
            _sets = EntitySet is null ? await Api.GetDataFilterSetsAsync() : [];
        });
    }
}
