using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components.Users;

/// <summary>
/// All roles of the organisation as one checklist, the user's ticked and on top; each click saves on its own (<see cref="PendingToggles{TKey}"/>), and a
/// clicked row keeps its place until the list is sorted again (<see cref="UserTabBase.Arrange"/>).
/// </summary>
public partial class UserRolesTab
{
    private readonly PendingToggles<Guid> _toggles = new();
    private HashSet<Guid> _saved = [];
    private IReadOnlyList<RoleRefDto> _roles = [];
    private HashSet<Guid> _top = [];
    private bool _canManage;
    private bool _canViewRoles;
    private string? _filter;

    [Inject] private Navigation.CoworkeePages Links { get; set; } = null!;

    protected override async Task PrepareAsync()
    {
        _canManage = await MayAsync(IdentityPermissions.Users.Manage);
        _canViewRoles = await MayAsync(IdentityPermissions.Roles.View);
    }

    protected override async Task LoadAsync()
    {
        // the roles of the organisation need Roles.View (or Users.Manage to give them); without, a reader sees the user's own
        var all = _canManage || _canViewRoles ? Api.GetRolesAsync() : Task.FromResult<IReadOnlyList<RoleDto>>([]);
        var own = (await Api.GetUserDetailAsync(UserId)).Roles;
        _saved = [.. own.Select(r => r.Id)];
        _roles = [.. (await all).Select(r => new RoleRefDto(r.Id, r.Name)).UnionBy(own, r => r.Id).OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    protected override void Arrange() => _top = [.. _roles.Where(r => Has(r.Id)).Select(r => r.Id)];

    private bool Has(Guid id) => _toggles.Shows(id, _saved.Contains(id));

    private void Filter(string? text)
    {
        _filter = text;
        Arrange();
    }

    private (List<RoleRefDto> Assigned, List<RoleRefDto> Others) Parts()
    {
        var shown = _roles.Where(r => string.IsNullOrWhiteSpace(_filter) || r.Name.Contains(_filter.Trim(), StringComparison.CurrentCultureIgnoreCase)).ToList();
        return ([.. shown.Where(r => _top.Contains(r.Id))], [.. shown.Where(r => !_top.Contains(r.Id))]);
    }

    private void Toggle(RoleRefDto role, bool on)
    {
        Clicked();
        _ = _toggles.ToggleAsync(role.Id, on,
            () => on ? Api.AddUserRoleAsync(UserId, role.Id) : Api.RemoveUserRoleAsync(UserId, role.Id),
            failure => SettledAsync(failure, role.Name, () =>
            {
                if (on)
                {
                    _saved.Add(role.Id);
                }
                else
                {
                    _saved.Remove(role.Id);
                }
            }));
    }
}
