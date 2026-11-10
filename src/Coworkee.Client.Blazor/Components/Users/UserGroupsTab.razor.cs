using Coworkee.Client.Blazor.Components.Groups;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components.Users;

/// <summary>
/// The groups a user is a direct member of, by their path ("Customer A / Hamburg"): filter, add through the group tree, remove; each click saves on its
/// own as a single membership (<see cref="PendingToggles{TKey}"/>), so another administrator's changes to the same user stay.
/// </summary>
public partial class UserGroupsTab
{
    private readonly PendingToggles<Guid> _toggles = new();
    private Dictionary<Guid, string> _saved = [];
    private readonly Dictionary<Guid, string> _picked = [];
    private bool _canManage;
    private bool _canViewGroups;
    private bool _picking;
    private string? _filter;

    [Inject] private Navigation.CoworkeePages Links { get; set; } = null!;

    [Inject] private Blazor.Data.IODataClient OData { get; set; } = null!;

    protected override async Task PrepareAsync()
    {
        _canManage = await MayAsync(IdentityPermissions.Groups.ManageMembers);
        _canViewGroups = _canManage || await MayAsync(IdentityPermissions.Groups.View);
    }

    protected override async Task LoadAsync()
    {
        var groups = (await Api.GetUserDetailAsync(UserId)).Groups;
        var paths = await PathsAsync([.. groups.Select(g => g.Id)]);
        _saved = groups.ToDictionary(g => g.Id, g => paths.GetValueOrDefault(g.Id) ?? g.Name);
    }

    // ponytail: the paths come from one OData query by id; a user in many hundreds of groups makes a long URL, page the ids then
    // the paths only label the rows: without them the names stay
    private async Task<Dictionary<Guid, string>> PathsAsync(IReadOnlyCollection<Guid> ids)
    {
        try
        {
            return _canViewGroups ? await GroupPaths.OfAsync(OData, ids) : [];
        }
        catch (Api.ApiException)
        {
            return [];
        }
    }

    private bool Has(Guid id) => _toggles.Shows(id, _saved.ContainsKey(id));

    private HashSet<Guid> Shown() => [.. _saved.Keys.Union(_toggles.PendingOn).Where(Has)];

    private IEnumerable<GroupRefDto> Rows() =>
        Shown().Select(id => new GroupRefDto(id, _saved.GetValueOrDefault(id) ?? _picked.GetValueOrDefault(id) ?? string.Empty))
            .Where(g => string.IsNullOrWhiteSpace(_filter) || g.Name.Contains(_filter.Trim(), StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase);

    private void Toggle((GroupNodeDto Node, bool Checked) pick)
    {
        _picked[pick.Node.Id] = pick.Node.Name;
        Toggle(pick.Node.Id, pick.Node.Name, pick.Checked);
    }

    private void Toggle(Guid id, string name, bool on) =>
        _ = _toggles.ToggleAsync(id, on,
            () => on ? Api.AddGroupMembersAsync(id, [UserId]) : Api.RemoveGroupMembersAsync(id, [UserId]),
            failure => SettledAsync(failure, name, () =>
            {
                if (on)
                {
                    _saved[id] = _saved.GetValueOrDefault(id) ?? name;
                }
                else
                {
                    _saved.Remove(id);
                }
            }));
}
