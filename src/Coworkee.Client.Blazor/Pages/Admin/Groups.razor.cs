using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Groups;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Data.Admin;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages.Admin;

/// <summary>The group tree on the left, the selected group in tabs on the right (General, Members, Roles, then the app's tabs); <c>?group=</c> selects one.</summary>
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
    private bool _ready;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private IODataClient OData { get; set; } = null!;

    [Inject] private PermissionStore Permissions { get; set; } = null!;

    [Inject] private IEnumerable<GroupDetailTab> AppTabs { get; set; } = null!;

    [Inject] private NavigationManager Navigation { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [SupplyParameterFromQuery(Name = "group")] public Guid? GroupId { get; set; }

    private string? ParentPath => _ancestors.Count == 0 ? null : string.Join(" / ", _ancestors.Select(a => a.Name));

    protected override async Task OnInitializedAsync()
    {
        _canManage = await Permissions.HasAsync(IdentityPermissions.Groups.Manage);
        _canManageMembers = await Permissions.HasAsync(IdentityPermissions.Groups.ManageMembers);
        foreach (var tab in AppTabs)
        {
            if (tab.Permission is null || await Permissions.HasAsync(tab.Permission))
            {
                _appTabs.Add(tab);
            }
        }

        if (GroupId is { } id)
        {
            await LoadAsync(id);
        }

        _ready = true;
    }

    // a global search hit while the page is open
    protected override async Task OnParametersSetAsync()
    {
        if (_ready && GroupId is { } id && id != _requested)
        {
            await LoadAsync(id);
            if (_group?.Code is { } code && _tree is not null)
            {
                await _tree.RevealAsync(code);
            }
        }
    }

    private async Task LoadAsync(Guid id) => await Snackbar.RunAsync(async () =>
    {
        _requested = id;
        var query = new ODataQuery { Filter = $"Id eq {id}", Top = 1, Count = false };
        _group = (await OData.QueryAsync<GroupRow>("Groups", query)).Items.FirstOrDefault(g => g.Id == id);
        _notFound = _group is null;
        _ancestors = _group?.Code is { } code && code.Contains('.') ? await AncestorsAsync(code) : [];
    });

    private async Task<List<(string? Code, string Name)>> AncestorsAsync(string code)
    {
        var prefixes = GroupPaths.Prefixes(code).SkipLast(1).ToList();
        var query = new ODataQuery { Filter = $"Code in ({string.Join(",", prefixes.Select(p => $"'{p}'"))})", Select = "Id,Name,Code", Count = false };
        var rows = (await OData.QueryAsync<GroupRow>("Groups", query)).Items;
        return [.. rows.Where(r => prefixes.Contains(r.Code!)).OrderBy(r => r.Code, StringComparer.Ordinal).Select(r => (r.Code, r.Name))];
    }

    private Task SelectAsync(GroupNodeDto node) => ShowAsync(node.Id);

    private async Task ShowAsync(Guid id)
    {
        await LoadAsync(id);
        Navigation.NavigateTo(Navigation.GetUriWithQueryParameter("group", id), replace: true);
    }

    private async Task CreateRootAsync()
    {
        if (await GroupForm.CreateAsync(Dialogs, Api, L["New root group"], null) is { } id)
        {
            await NodeChangedAsync(id);
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
