using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data.Import;
using Coworkee.Client.Blazor.Components.Groups;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Data.Admin;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Data;
using Coworkee.Contracts.DataFilters;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages.Admin;

/// <summary>The group tree on the left, the selected group in tabs on the right (General, Members, Roles, Data filters, then the app's tabs); <c>?group=</c> selects one.</summary>
public partial class Groups
{
    private GroupTree? _tree;
    private GroupRow? _group;
    private Guid? _requested;
    private bool _notFound;
    private List<(string? Code, string Name)> _ancestors = [];
    private List<GroupDetailTab> _appTabs = [];
    private bool _canManage;
    private bool _canManageMembers;
    private bool _canManageDataFilters;
    private bool _ready;
    private bool _loadingGroup;
    private Dictionary<Guid, int> _counts = [];
    private bool _onlyFiltered;
    private IReadOnlyList<GroupNodeDto>? _filtered;
    private int _tabIndex;
    private const string DataFiltersTab = "data-filters";
    private const int DataFiltersIndex = 3;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private IODataClient OData { get; set; } = null!;

    [Inject] private PermissionStore Permissions { get; set; } = null!;

    [Inject] private IEnumerable<GroupDetailTab> AppTabs { get; set; } = null!;

    [Inject] private NavigationManager Navigation { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private IServiceProvider Services { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [SupplyParameterFromQuery(Name = "group")] public Guid? GroupId { get; set; }

    /// <summary><c>data-filters</c> opens the group's Data filters tab.</summary>
    [SupplyParameterFromQuery(Name = "tab")] public string? Tab { get; set; }

    private string? ParentPath => _ancestors.Count == 0 ? null : string.Join(" / ", _ancestors.Select(a => a.Name));

    protected override async Task OnInitializedAsync()
    {
        _canManage = await Permissions.HasAsync(IdentityPermissions.Groups.Manage);
        _canManageMembers = await Permissions.HasAsync(IdentityPermissions.Groups.ManageMembers);
        _canManageDataFilters = await Permissions.HasAsync(DataFilterPermissions.ManageGroups);
        foreach (var tab in AppTabs)
        {
            if (tab.Permission is null || await Permissions.HasAsync(tab.Permission))
            {
                _appTabs.Add(tab);
            }
        }

        if (_canManageDataFilters)
        {
            await LoadCountsAsync();
        }

        if (GroupId is { } id)
        {
            await LoadAsync(id);
            _tabIndex = TabIndex(Tab);
        }

        _ready = true;
    }

    // a global search hit while the page is open
    protected override async Task OnParametersSetAsync()
    {
        if (_ready && GroupId is { } id && id != _requested)
        {
            await LoadAsync(id);
            _tabIndex = TabIndex(Tab);
            if (_group?.Code is { } code && _tree is not null)
            {
                await _tree.RevealAsync(code);
            }
        }
    }

    // the bar shows while the chosen group loads; the previous one stays until the new one is there
    private async Task LoadAsync(Guid id)
    {
        _loadingGroup = true;
        StateHasChanged();
        try
        {
            await Snackbar.RunAsync(async () =>
            {
                _requested = id;
                var query = new ODataQuery { Filter = $"Id eq {id}", Top = 1, Count = false };
                var group = (await OData.QueryAsync<GroupRow>("Groups", query)).Items.FirstOrDefault(g => g.Id == id);
                _ancestors = group?.Code is { } code && code.Contains('.') ? await AncestorsAsync(code) : [];
                (_group, _notFound) = (group, group is null);
            });
        }
        finally
        {
            _loadingGroup = false;
        }
    }

    private async Task<List<(string? Code, string Name)>> AncestorsAsync(string code)
    {
        var prefixes = GroupPaths.Prefixes(code).SkipLast(1).ToList();
        var query = new ODataQuery { Filter = $"Code in ({string.Join(",", prefixes.Select(p => $"'{p}'"))})", Select = "Id,Name,Code", Count = false };
        var rows = (await OData.QueryAsync<GroupRow>("Groups", query)).Items;
        return [.. rows.Where(r => prefixes.Contains(r.Code!)).OrderBy(r => r.Code, StringComparer.Ordinal).Select(r => (r.Code, r.Name))];
    }

    private Task SelectAsync(GroupNodeDto node) => ShowAsync(node.Id);

    private int TabIndex(string? tab) => tab == DataFiltersTab && _canManageDataFilters ? DataFiltersIndex : 0;

    private async Task ShowAsync(Guid id, string? tab = null)
    {
        await LoadAsync(id);
        _tabIndex = TabIndex(tab);
        Navigation.NavigateTo(Navigation.GetUriWithQueryParameters(new Dictionary<string, object?> { ["group"] = id, ["tab"] = tab }), replace: true);
    }

    // one load of every group filter of the tenant, counted per group: no request per node
    private async Task LoadCountsAsync()
    {
        await Snackbar.RunAsync(async () =>
        {
            var filters = await Api.GetDataFiltersAsync(layer: DataFilterLayer.Group);
            _counts = filters.Where(f => f.LayerKey is not null).GroupBy(f => f.LayerKey!.Value).ToDictionary(g => g.Key, g => g.Count());
            await LoadFilteredAsync();
        });
    }

    // the filtered groups as a flat list named by their paths
    private async Task LoadFilteredAsync()
    {
        if (!_onlyFiltered)
        {
            return;
        }

        var groups = (await GroupPaths.OfAsync(OData, [.. _counts.Keys])).ToList();
        _filtered = [.. groups.OrderBy(g => g.Value, StringComparer.CurrentCultureIgnoreCase).Select(g => new GroupNodeDto(g.Key, null, g.Key.ToString(), g.Value, null, 0, 0))];
    }

    private async Task ToggleOnlyFilteredAsync()
    {
        _onlyFiltered = !_onlyFiltered;
        _filtered = null;
        await Snackbar.RunAsync(LoadFilteredAsync);
        if (_onlyFiltered && _filtered is null)
        {
            _onlyFiltered = false;
        }
    }

    private Task FiltersChangedAsync() => InvokeAsync(async () =>
    {
        await LoadCountsAsync();
        StateHasChanged();
    });

    private async Task CreateRootAsync()
    {
        if (await GroupForm.CreateAsync(Dialogs, Api, L["New root group"], null) is { } id)
        {
            await NodeChangedAsync(id);
        }
    }

    private async Task ImportAsync()
    {
        if (await Dialogs.ShowImportWizardAsync(L["Import"], "Groups") && _tree is not null)
        {
            await _tree.ReloadAsync();
        }
    }

    // parents before children and the columns the group import reads, so the file imports again (also into another tenant)
    private async Task ExportAsync()
    {
        ExportResult? result = null;
        var query = new ODataQuery { OrderBy = "Code" };
        if (await Snackbar.RunAsync(async () => result = await Services.GetRequiredService<IDataTransferApi>().ExportAsync("Groups", ExportFileFormat.Xlsx, query, ["Name", "Description", "ParentPath"])))
        {
            await (result!.Job is { } job
                ? Services.GetRequiredService<DataTransferWatcher>().Announce(job)
                : Services.GetRequiredService<FileDownloader>().DownloadAsync(result.FileName, result.ContentType, result.Content!));
        }
    }

    private async Task NodeChangedAsync(Guid id)
    {
        await ShowAsync(id);
        if (_tree is not null)
        {
            await _tree.ReloadAsync();
            await _tree.RevealAsync(_group?.Code ?? string.Empty);
        }
    }

    private async Task ChangedAsync()
    {
        if (_group is { } group)
        {
            await NodeChangedAsync(group.Id);
        }
    }

    // someone else changed groups, members or roles: reload what is shown, keep the selection
    private async Task RefreshAsync()
    {
        if (_requested is { } id)
        {
            await LoadAsync(id);
        }

        if (_tree is not null)
        {
            await _tree.ReloadAsync();
        }
    }

    private async Task DeletedAsync(GroupNodeDto node)
    {
        if (_group?.Code is { } code && GroupPaths.Prefixes(code).Contains(node.Code))
        {
            (_group, _requested) = (null, null);
            Navigation.NavigateTo(Navigation.GetUriWithQueryParameter("group", (Guid?)null), replace: true);
        }

        if (_tree is not null)
        {
            await _tree.ReloadAsync();
        }
    }
}
