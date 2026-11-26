using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Groups;

/// <summary>
/// The group tree as one virtualized list of rows: all top groups load at once, children as a node opens, a search shows the matches with their ancestors.
/// Only the rows in view are rendered, so thousands of top groups (an imported organisation) scroll without paging; MudTreeView rendered every node and took seconds per click.
/// Used by the groups page, the group picker dialog and, with checkboxes, the group scope picker.
/// </summary>
public partial class GroupTree
{
    /// <summary>Height of a row in pixels; fixed, as the virtualization needs it.</summary>
    public const int RowHeight = 36;

    private List<GroupNodeDto> _roots = [];
    // children of the nodes opened so far (a search: of the matching branches); a node closed and opened again keeps them
    private readonly Dictionary<Guid, List<GroupNodeDto>> _children = [];
    private readonly HashSet<Guid> _expanded = [];
    private List<Row> _rows = [];
    private List<Guid> _matches = [];
    private int _busy;
    private string _search = string.Empty;
    private int _loads;
    private IReadOnlyList<GroupNodeDto>? _nodes;
    private ElementReference _list;
    private int? _scrollTo;

    [Inject] private IJSRuntime JS { get; set; } = null!;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter] public Guid? SelectedId { get; set; }

    [Parameter] public EventCallback<GroupNodeDto> SelectedChanged { get; set; }

    /// <summary>Opens the tree down to the group with this code on the first load.</summary>
    [Parameter] public string? RevealCode { get; set; }

    /// <summary>A group that cannot be picked, e.g. the one being moved.</summary>
    [Parameter] public Guid? Exclude { get; set; }

    /// <summary>Leaves <see cref="Exclude"/> out with its subtree (a group cannot move below itself); otherwise only the group itself is disabled.</summary>
    [Parameter] public bool ExcludeSubtree { get; set; } = true;

    [Parameter] public RenderFragment<GroupNodeDto>? NodeActions { get; set; }

    /// <summary>A fixed set of groups (e.g. the user's own) shown and searched without the API; without it the tree loads from the server.</summary>
    [Parameter] public IReadOnlyList<GroupNodeDto>? Nodes { get; set; }

    /// <summary>Checkbox mode: the checked groups; a click on a row or its checkbox raises <see cref="CheckedIdsChanged"/> instead of selecting the node.</summary>
    [Parameter] public IReadOnlySet<Guid>? CheckedIds { get; set; }

    [Parameter] public EventCallback<IReadOnlySet<Guid>> CheckedIdsChanged { get; set; }

    /// <summary>Checkbox mode, one click at a time: the group and whether it is checked now, instead of <see cref="CheckedIdsChanged"/>; the user page saves each on its own.</summary>
    [Parameter] public EventCallback<(GroupNodeDto Node, bool Checked)> NodeChecked { get; set; }

    /// <summary>Groups whose name is shown bold, e.g. direct memberships.</summary>
    [Parameter] public IReadOnlySet<Guid>? Bold { get; set; }

    /// <summary>Style of the area below the search, e.g. <c>max-height:60vh;overflow:auto</c>: the list scrolls (and renders only the rows in view), the search field stays in view.</summary>
    [Parameter] public string? ListStyle { get; set; }

    /// <summary>The groups listed now, for "Select all": every row without a search, only the matches (not their ancestors) with one.</summary>
    public IReadOnlyList<Guid> Listed() => _search.Length == 0 ? [.. _rows.Select(r => r.Node.Id)] : _matches;

    protected override async Task OnParametersSetAsync()
    {
        // new fixed groups (the scope state reloaded) rebuild the tree; the first load happens in OnInitializedAsync
        if (Nodes is not null && _nodes is not null && !ReferenceEquals(Nodes, _nodes))
        {
            await LoadAsync();
        }

        _nodes = Nodes;
    }

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
        if (RevealCode is not null)
        {
            await RevealAsync(RevealCode);
        }
    }

    /// <summary>Loads again after a change and keeps open nodes open.</summary>
    public async Task ReloadAsync()
    {
        var expanded = _expanded.ToHashSet();
        if (await LoadAsync() && _search.Length == 0)
        {
            await OpenAsync(_roots, expanded);
            Flatten();
        }

        StateHasChanged();
    }

    /// <summary>Opens the ancestors of the group with this code, level by level, and scrolls the group into view.</summary>
    public async Task RevealAsync(string code)
    {
        var level = _roots;
        foreach (var prefix in GroupPaths.Prefixes(code).SkipLast(1))
        {
            // ancestors above the roots of a group-scoped administrator's reach are not in the tree
            if (level.Find(n => n.Code == prefix) is not { } node)
            {
                continue;
            }

            if (await ChildrenOfAsync(node) is not { } children)
            {
                break;
            }

            _expanded.Add(node.Id);
            level = children;
        }

        Flatten();
        _scrollTo = _rows.FindIndex(r => r.Node.Code == code) is >= 0 and var index ? index : null;
        StateHasChanged();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_scrollTo is not { } index)
        {
            return;
        }

        _scrollTo = null;
        try
        {
            var module = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/Coworkee.Client.Blazor/coworkee.js");
            await module.InvokeVoidAsync("scrollIntoRange", _list, index * RowHeight, RowHeight);
        }
        catch (JSDisconnectedException)
        {
        }
    }

    /// <returns><c>false</c> when the load failed (the shown nodes stay) or a later load (a newer search text) overtook it.</returns>
    private async Task<bool> LoadAsync()
    {
        var load = ++_loads;
        var search = _search;
        IReadOnlyList<GroupNodeDto>? nodes;
        _busy++;
        try
        {
            nodes = Nodes is { } fixedNodes ? GroupPaths.Matching(fixedNodes, search)
                : await FetchAsync(() => search.Length == 0 ? Api.GetGroupChildrenAsync(null) : Api.SearchGroupTreeAsync(search));
        }
        finally
        {
            _busy--;
        }

        if (nodes is null || load != _loads)
        {
            return false;
        }

        _children.Clear();
        _expanded.Clear();
        _matches = [.. nodes.Where(n => search.Length > 0 && n.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)).Select(n => n.Id)];
        if (search.Length == 0 && Nodes is null)
        {
            _roots = [.. nodes.Where(Shown)];
        }
        else
        {
            Tree(nodes);
        }

        Flatten();
        return true;
    }

    // the matches with their ancestors (or the fixed groups) as an open tree: every node with children among them is open
    private void Tree(IReadOnlyList<GroupNodeDto> nodes)
    {
        // the excluded group comes along as ancestor of matches below it: leave out its whole subtree
        var excluded = ExcludeSubtree ? nodes.FirstOrDefault(n => n.Id == Exclude)?.Code : null;
        var shown = nodes.Where(n => excluded is null || !GroupPaths.Prefixes(n.Code).Contains(excluded)).ToDictionary(n => n.Id);
        foreach (var level in shown.Values.Where(n => n.ParentId is { } parentId && shown.ContainsKey(parentId)).GroupBy(n => n.ParentId!.Value))
        {
            _children[level.Key] = [.. level.OrderBy(n => n.Name)];
            _expanded.Add(level.Key);
        }

        _roots = [.. shown.Values.Where(n => n.ParentId is not { } parentId || !shown.ContainsKey(parentId)).OrderBy(n => n.Name)];
    }

    /// <summary>The rows of the open tree in display order: a few thousand are rebuilt in well under a millisecond.</summary>
    private void Flatten()
    {
        var rows = new List<Row>(_roots.Count);
        Add(_roots, 0);
        _rows = rows;

        void Add(List<GroupNodeDto> level, int depth)
        {
            foreach (var node in level)
            {
                rows.Add(new Row(node, depth));
                if (_expanded.Contains(node.Id) && _children.TryGetValue(node.Id, out var children))
                {
                    Add(children, depth + 1);
                }
            }
        }
    }

    private async Task SearchAsync(string? text)
    {
        _search = text?.Trim() ?? string.Empty;
        await LoadAsync();
    }

    private async Task ExpandAsync(GroupNodeDto node)
    {
        if (!_expanded.Remove(node.Id))
        {
            var load = _loads;
            // a newer load (search) replaced the tree meanwhile
            if (await ChildrenOfAsync(node) is null || load != _loads)
            {
                return;
            }

            _expanded.Add(node.Id);
        }

        Flatten();
    }

    /// <returns>The children (from the server the first time), <c>null</c> when the API failed.</returns>
    private async Task<List<GroupNodeDto>?> ChildrenOfAsync(GroupNodeDto node)
    {
        if (_children.TryGetValue(node.Id, out var known))
        {
            return known;
        }

        // the fixed groups are all there: what is not among them is not shown
        if (Nodes is not null)
        {
            return _children[node.Id] = [];
        }

        _busy++;
        StateHasChanged();
        try
        {
            return await FetchAsync(() => Api.GetGroupChildrenAsync(node.Id)) is { } children ? _children[node.Id] = [.. children.Where(Shown)] : null;
        }
        finally
        {
            _busy--;
        }
    }

    private async Task OpenAsync(List<GroupNodeDto> level, HashSet<Guid> expanded)
    {
        foreach (var node in level.Where(n => expanded.Contains(n.Id)).ToList())
        {
            if (await ChildrenOfAsync(node) is { } children)
            {
                _expanded.Add(node.Id);
                await OpenAsync(children, expanded);
            }
        }
    }

    /// <returns><c>null</c> when the API failed; the snackbar shows why.</returns>
    private async Task<IReadOnlyList<GroupNodeDto>?> FetchAsync(Func<Task<IReadOnlyList<GroupNodeDto>>> load)
    {
        IReadOnlyList<GroupNodeDto>? nodes = null;
        await Snackbar.RunAsync(async () => nodes = await load());
        return nodes;
    }

    private bool Shown(GroupNodeDto node) => !ExcludeSubtree || node.Id != Exclude;

    private bool Disabled(GroupNodeDto node) => node.Id == Exclude;

    private string RowClass(GroupNodeDto node) =>
        $"cw-tree-row{(node.Id == SelectedId ? " selected" : null)}{(Disabled(node) ? " disabled" : null)}";

    // a click on the row selects it, or checks it in checkbox mode
    private Task ActivateAsync(GroupNodeDto node) =>
        Disabled(node) ? Task.CompletedTask : CheckedIds is not null ? ToggleAsync(node, !CheckedIds.Contains(node.Id)) : SelectedChanged.InvokeAsync(node);

    private Task KeyAsync(KeyboardEventArgs e, GroupNodeDto node) => e.Key switch
    {
        "Enter" => ActivateAsync(node),
        "ArrowRight" when node.ChildCount > 0 && !_expanded.Contains(node.Id) => ExpandAsync(node),
        "ArrowLeft" when _expanded.Contains(node.Id) => ExpandAsync(node),
        _ => Task.CompletedTask,
    };

    private Task ToggleAsync(GroupNodeDto node, bool on)
    {
        if (NodeChecked.HasDelegate)
        {
            return NodeChecked.InvokeAsync((node, on));
        }

        HashSet<Guid> ids = CheckedIds?.ToHashSet() ?? [];
        if (on)
        {
            ids.Add(node.Id);
        }
        else
        {
            ids.Remove(node.Id);
        }

        return CheckedIdsChanged.InvokeAsync(ids);
    }

    /// <summary>A row of the list: a group and its depth in the open tree.</summary>
    private sealed record Row(GroupNodeDto Node, int Depth);
}
