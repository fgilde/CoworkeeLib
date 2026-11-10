using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Groups;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components.Users;

/// <summary>
/// The groups of a user as checklists: on top the groups he is a direct member of, by their path ("Customer A / Hamburg"); below the other groups, as a
/// lazy tree or, with filter text, the search matches of the server. Each click saves on its own as a single membership (<see cref="PendingToggles{TKey}"/>),
/// so another administrator's changes to the same user stay; a clicked row keeps its place until the lists are sorted again (<see cref="UserTabBase.Arrange"/>).
/// </summary>
public partial class UserGroupsTab
{
    // the matches SearchGroupTree returns at most
    private const int MaxMatches = 200;
    private readonly PendingToggles<Guid> _toggles = new();
    private Dictionary<Guid, string> _saved = [];
    // paths of the groups seen below, for the ones ticked there once they move up
    private readonly Dictionary<Guid, string> _paths = [];
    private readonly HashSet<Guid> _expanded = [];
    private List<Row> _others = [];
    private HashSet<Guid> _top = [];
    private bool _canManage;
    private bool _canViewGroups;
    private bool _searching;
    private bool _truncated;
    private bool _busy;
    private int _loads;
    private string? _filter;

    [Inject] private Navigation.CoworkeePages Links { get; set; } = null!;

    [Inject] private Blazor.Data.IODataClient OData { get; set; } = null!;

    protected override async Task PrepareAsync()
    {
        _canManage = await MayAsync(IdentityPermissions.Groups.ManageMembers);
        _canViewGroups = _canManage || await MayAsync(IdentityPermissions.Groups.View);
        await LoadOthersAsync();
    }

    protected override async Task LoadAsync()
    {
        var groups = (await Api.GetUserDetailAsync(UserId)).Groups;
        var paths = await PathsAsync([.. groups.Select(g => g.Id)]);
        _saved = groups.ToDictionary(g => g.Id, g => paths.GetValueOrDefault(g.Id) ?? g.Name);
    }

    protected override void Arrange() => _top = [.. Shown()];

    // the paths only label the rows: without them the names stay
    private async Task<Dictionary<Guid, string>> PathsAsync(IReadOnlyCollection<Guid> ids)
    {
        try
        {
            return _canViewGroups ? await GroupPaths.OfAsync(OData, ids) : [];
        }
        catch (ApiException)
        {
            return [];
        }
    }

    private bool Has(Guid id) => _toggles.Shows(id, _saved.ContainsKey(id));

    private IEnumerable<Guid> Shown() => _saved.Keys.Union(_toggles.PendingOn).Where(Has);

    private string PathOf(Guid id) => _saved.GetValueOrDefault(id) ?? _paths.GetValueOrDefault(id) ?? string.Empty;

    private List<(Guid Id, string Path)> Members() =>
        [.. _top.Select(id => (id, PathOf(id)))
            .Where(g => string.IsNullOrWhiteSpace(_filter) || g.Item2.Contains(_filter.Trim(), StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(g => g.Item2, StringComparer.CurrentCultureIgnoreCase)];

    // the member groups are on top; in the tree a member group with subgroups stays, so they can be opened
    private List<Row> Others() => [.. _others.Where(r => !_top.Contains(r.Node.Id) || (!_searching && r.Node.ChildCount > 0))];

    private async Task FilterAsync(string? text)
    {
        _filter = text;
        Arrange();
        await LoadOthersAsync();
    }

    /// <summary>The roots of the tree, or the matches of the filter text with their paths; a later filter text overtakes an earlier load.</summary>
    private async Task LoadOthersAsync()
    {
        if (!_canManage)
        {
            return;
        }

        var load = ++_loads;
        var search = _filter?.Trim() ?? string.Empty;
        _busy = true;
        var nodes = await FetchAsync(() => search.Length == 0 ? Api.GetGroupChildrenAsync(null) : Api.SearchGroupTreeAsync(search));
        if (load != _loads)
        {
            return;
        }

        _busy = false;
        if (nodes is null)
        {
            return;
        }

        _expanded.Clear();
        _searching = search.Length > 0;
        if (_searching)
        {
            // the server sends the matches with their ancestors: show the matches flat, by path
            var named = nodes.Select(n => ((string?)n.Code, n.Name)).ToList();
            var matches = nodes.Where(n => n.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)).ToList();
            _truncated = matches.Count >= MaxMatches;
            _others = [.. matches.Select(n => new Row(n, GroupPaths.Path(n.Code, n.Name, named), 0)).OrderBy(r => r.Path, StringComparer.CurrentCultureIgnoreCase)];
        }
        else
        {
            _truncated = false;
            _others = [.. nodes.Select(n => new Row(n, n.Name, 0))];
        }

        Remember(_others);
    }

    private async Task ExpandAsync(Row row)
    {
        if (_expanded.Remove(row.Node.Id))
        {
            var below = row.Node.Code + ".";
            _others.RemoveAll(r => r.Node.Code.StartsWith(below, StringComparison.Ordinal));
            _expanded.RemoveWhere(id => !_others.Exists(r => r.Node.Id == id));
            return;
        }

        var load = _loads;
        var children = await FetchAsync(() => Api.GetGroupChildrenAsync(row.Node.Id));
        var index = _others.IndexOf(row);
        // a new filter text replaced the list meanwhile
        if (children is null || load != _loads || index < 0)
        {
            return;
        }

        List<Row> rows = [.. children.Select(c => new Row(c, $"{row.Path} / {c.Name}", row.Depth + 1))];
        _others.InsertRange(index + 1, rows);
        _expanded.Add(row.Node.Id);
        Remember(rows);
    }

    private void Remember(IEnumerable<Row> rows)
    {
        foreach (var row in rows)
        {
            _paths[row.Node.Id] = row.Path;
        }
    }

    /// <returns><c>null</c> when the API failed; the snackbar shows why.</returns>
    private async Task<IReadOnlyList<GroupNodeDto>?> FetchAsync(Func<Task<IReadOnlyList<GroupNodeDto>>> load)
    {
        IReadOnlyList<GroupNodeDto>? nodes = null;
        await Snackbar.RunAsync(async () => nodes = await load());
        return nodes;
    }

    private void Toggle(Guid id, string path, bool on)
    {
        Clicked();
        _ = _toggles.ToggleAsync(id, on,
            () => on ? Api.AddGroupMembersAsync(id, [UserId]) : Api.RemoveGroupMembersAsync(id, [UserId]),
            failure => SettledAsync(failure, path, () =>
            {
                if (on)
                {
                    _saved[id] = _saved.GetValueOrDefault(id) ?? path;
                }
                else
                {
                    _saved.Remove(id);
                }
            }));
    }

    /// <summary>A group of the lower list: its path from the roots of the tree (or of the search) and its depth in the tree.</summary>
    private sealed record Row(GroupNodeDto Node, string Path, int Depth);
}
