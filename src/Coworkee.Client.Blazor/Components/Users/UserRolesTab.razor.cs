using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components.Users;

/// <summary>The roles a user has directly: filter, add from a filterable list, remove; each click saves on its own (<see cref="PendingToggles{TKey}"/>).</summary>
public partial class UserRolesTab
{
    private readonly PendingToggles<Guid> _toggles = new();
    private Dictionary<Guid, string> _saved = [];
    private IReadOnlyList<RoleDto> _all = [];
    private bool _canManage;
    private bool _picking;
    private string? _filter;
    private string? _pickFilter;

    [Inject] private Navigation.CoworkeePages Links { get; set; } = null!;

    protected override async Task PrepareAsync() => _canManage = await MayAsync(IdentityPermissions.Users.Manage);

    protected override async Task LoadAsync()
    {
        // the choices only matter to those who may change the roles; reading the roles of the organisation needs Roles.View
        var all = _canManage ? Api.GetRolesAsync() : Task.FromResult<IReadOnlyList<RoleDto>>([]);
        _saved = (await Api.GetUserDetailAsync(UserId)).Roles.ToDictionary(r => r.Id, r => r.Name);
        _all = [.. (await all).OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    private bool Has(Guid id) => _toggles.Shows(id, _saved.ContainsKey(id));

    private static bool Matches(string text, string? filter) => string.IsNullOrWhiteSpace(filter) || text.Contains(filter.Trim(), StringComparison.CurrentCultureIgnoreCase);

    // saved roles and the ones added but not saved yet, without the ones removed but not saved yet
    private IEnumerable<RoleRefDto> Rows() =>
        _saved.Keys.Union(_toggles.PendingOn).Where(Has)
            .Select(id => new RoleRefDto(id, _saved.GetValueOrDefault(id) ?? _all.FirstOrDefault(r => r.Id == id)?.Name ?? string.Empty))
            .Where(r => Matches(r.Name, _filter))
            .OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase);

    private void Toggle(Guid id, string name, bool on) =>
        _ = _toggles.ToggleAsync(id, on,
            () => on ? Api.AddUserRoleAsync(UserId, id) : Api.RemoveUserRoleAsync(UserId, id),
            failure => SettledAsync(failure, name, () =>
            {
                if (on)
                {
                    _saved[id] = name;
                }
                else
                {
                    _saved.Remove(id);
                }
            }));
}
