using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Groups;

/// <summary>
/// App-bar button for the groups the user works in: "All my groups", one group's name or "{0} groups"; opens a tree with checkboxes.
/// The checks are a draft, saved once when the list closes (a click outside or on the button): every check used to reload the page's data while the user was still choosing.
/// Not part of the layout: apps that use groups as units place <c>&lt;GroupScopePicker /&gt;</c> themselves. Hidden without groups.
/// </summary>
public partial class GroupScopePicker : IDisposable
{
    /// <summary>Where the list opens: cascaded by the app bar the picker sits in; elsewhere below its start.</summary>
    [CascadingParameter] private Navigation.HeaderMenuOrigins? HeaderOrigins { get; set; }

    private Navigation.HeaderMenuOrigins MenuOrigins => HeaderOrigins ?? new(MudBlazor.Origin.BottomLeft, MudBlazor.Origin.TopLeft);

    private bool _scopeAll;
    private bool _open;
    private IReadOnlyList<GroupNodeDto> _nodes = [];
    private IReadOnlySet<Guid> _checked = new HashSet<Guid>();
    private IReadOnlySet<Guid> _draft = new HashSet<Guid>();
    private bool _saving;

    [Inject] private GroupScopeState State { get; set; } = null!;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private PermissionStore Permissions { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    // with ScopeAll every group of the tenant can be picked: the tree loads from the server instead of the user's list
    private bool Visible => _scopeAll || State.Groups.Count > 0;

    private IReadOnlySet<Guid> Members => State.Groups.Where(g => g.IsMember).Select(g => g.Id).ToHashSet();

    private string Label => State.Groups.Where(g => g.Selected).ToList() switch
    {
        [] => L["All my groups"],
        [var one] => one.Name,
        var many => L["{0} groups", many.Count],
    };

    protected override async Task OnInitializedAsync()
    {
        State.Changed += Refresh;
        _scopeAll = await Permissions.HasAsync(IdentityPermissions.Groups.ScopeAll);
        await State.LoadAsync();
        if (_scopeAll && State.Groups.Count == 0)
        {
            // no membership, no selection: one look at the roots tells whether the tenant has groups at all
            _scopeAll = await HasGroupsAsync();
        }

        Rebuild();
    }

    public void Dispose() => State.Changed -= Refresh;

    private void Refresh() => _ = InvokeAsync(() =>
    {
        Rebuild();
        StateHasChanged();
    });

    private void Rebuild()
    {
        var children = State.Groups.ToLookup(g => g.ParentId);
        _nodes = [.. State.Groups.Select(g => new GroupNodeDto(g.Id, g.ParentId, g.Code, g.Name, null, children[g.Id].Count(), 0))];
        _checked = State.Groups.Where(g => g.Selected).Select(g => g.Id).ToHashSet();
    }

    private async Task<bool> HasGroupsAsync()
    {
        try
        {
            return (await Api.GetGroupChildrenAsync(null)).Count > 0;
        }
        catch (ApiException)
        {
            return false;
        }
    }

    private Task ToggleAsync()
    {
        if (_open)
        {
            return CloseAsync();
        }

        _draft = _checked;
        _open = true;
        return Task.CompletedTask;
    }

    // the draft is saved once, when the list closes; the button shows the save, a rejected one restores the stored selection
    private async Task CloseAsync()
    {
        _open = false;
        if (_draft.SetEquals(_checked))
        {
            return;
        }

        var ids = _draft;
        (_checked, _saving) = (ids, true);
        StateHasChanged();
        await Task.Yield();
        try
        {
            if (!await Snackbar.RunAsync(() => State.SelectAsync([.. ids])))
            {
                Rebuild();
            }
        }
        finally
        {
            _saving = false;
        }
    }
}
