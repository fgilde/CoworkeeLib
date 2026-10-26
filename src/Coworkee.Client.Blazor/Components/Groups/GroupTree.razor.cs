using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Groups;

/// <summary>
/// The group tree, loaded level by level as nodes open (MudBlazor's <c>ServerData</c>); a search shows the matches with their ancestors.
/// Used by the groups page, the group picker dialog and, with checkboxes, the group scope picker.
/// </summary>
public partial class GroupTree
{
    private List<TreeItemData<GroupNodeDto>> _items = [];
    private string _search = string.Empty;
    private int _loads;
    private IReadOnlyList<GroupNodeDto>? _nodes;

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

    /// <summary>Checkbox mode: the checked groups; a click on a checkbox raises <see cref="CheckedIdsChanged"/> instead of selecting the node.</summary>
    [Parameter] public IReadOnlySet<Guid>? CheckedIds { get; set; }

    [Parameter] public EventCallback<IReadOnlySet<Guid>> CheckedIdsChanged { get; set; }

    /// <summary>Groups whose name is shown bold, e.g. direct memberships.</summary>
    [Parameter] public IReadOnlySet<Guid>? Bold { get; set; }

    private GroupNodeDto? Selected => Flatten(_items).FirstOrDefault(i => i.Value!.Id == SelectedId)?.Value;

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
        var expanded = Flatten(_items).Where(i => i.Expanded).Select(i => i.Value!.Id).ToHashSet();
        if (await LoadAsync() && _search.Length == 0)
        {
            await ExpandAsync(_items, expanded);
        }

        StateHasChanged();
    }

    /// <summary>Opens the ancestors of the group with this code, level by level.</summary>
    public async Task RevealAsync(string code)
    {
        var level = _items;
        foreach (var prefix in GroupPaths.Prefixes(code).SkipLast(1))
        {
            if (level.FirstOrDefault(i => i.Value!.Code == prefix) is not { } item)
            {
                break;
            }

            var children = item.Children is null ? await ChildrenOfAsync(item.Value!.Id) : [.. Typed(item.Children)];
            (item.Children, item.Expanded) = (children, true);
            level = children;
        }

        StateHasChanged();
    }

    /// <returns><c>false</c> when the load failed (the shown nodes stay) or a later load (a newer search text) overtook it.</returns>
    private async Task<bool> LoadAsync()
    {
        var load = ++_loads;
        var search = _search;
        var nodes = Nodes is { } fixedNodes ? GroupPaths.Matching(fixedNodes, search)
            : await FetchAsync(() => search.Length == 0 ? Api.GetGroupChildrenAsync(null) : Api.SearchGroupTreeAsync(search));
        if (nodes is null || load != _loads)
        {
            return false;
        }

        _items = search.Length == 0 && Nodes is null ? [.. nodes.Where(Shown).Select(Item)] : Tree(nodes);
        return true;
    }

    private async Task SearchAsync(string? text)
    {
        _search = text?.Trim() ?? string.Empty;
        await LoadAsync();
    }

    private async Task<IReadOnlyCollection<TreeItemData<GroupNodeDto>>> ChildrenAsync(GroupNodeDto? parent) => await ChildrenOfAsync(parent?.Id);

    private async Task<List<TreeItemData<GroupNodeDto>>> ChildrenOfAsync(Guid? parentId) =>
        [.. (await FetchAsync(() => Api.GetGroupChildrenAsync(parentId)) ?? []).Where(Shown).Select(Item)];

    /// <returns><c>null</c> when the API failed; the snackbar shows why.</returns>
    private async Task<IReadOnlyList<GroupNodeDto>?> FetchAsync(Func<Task<IReadOnlyList<GroupNodeDto>>> load)
    {
        IReadOnlyList<GroupNodeDto>? nodes = null;
        await Snackbar.RunAsync(async () => nodes = await load());
        return nodes;
    }

    private async Task ExpandAsync(List<TreeItemData<GroupNodeDto>> level, HashSet<Guid> expanded)
    {
        foreach (var item in level.Where(i => expanded.Contains(i.Value!.Id)))
        {
            var children = await ChildrenOfAsync(item.Value!.Id);
            (item.Children, item.Expanded) = (children, true);
            await ExpandAsync(children, expanded);
        }
    }

    // ponytail: a search shows only the matching branches; an ancestor opened there lists its other children after the search is cleared
    private List<TreeItemData<GroupNodeDto>> Tree(IReadOnlyList<GroupNodeDto> nodes)
    {
        // the excluded group comes along as ancestor of matches below it: leave out its whole subtree
        var excluded = ExcludeSubtree ? nodes.FirstOrDefault(n => n.Id == Exclude)?.Code : null;
        var items = nodes.Where(n => excluded is null || !GroupPaths.Prefixes(n.Code).Contains(excluded)).ToDictionary(n => n.Id, Item);
        var children = items.Values.ToLookup(i => i.Value!.ParentId);
        foreach (var parent in items.Values.Where(p => children.Contains(p.Value!.Id)))
        {
            (parent.Children, parent.Expanded) = (children[parent.Value!.Id].OrderBy(i => i.Value!.Name).ToList(), true);
        }

        return [.. items.Values.Where(i => i.Value!.ParentId is not { } parentId || !items.ContainsKey(parentId)).OrderBy(i => i.Value!.Name)];
    }

    private bool Shown(GroupNodeDto node) => !ExcludeSubtree || node.Id != Exclude;

    private bool Disabled(GroupNodeDto node) => node.Id == Exclude;

    private static TreeItemData<GroupNodeDto> Item(GroupNodeDto node) => new() { Value = node, Text = node.Name, Expandable = node.ChildCount > 0 };

    private static IEnumerable<TreeItemData<GroupNodeDto>> Flatten(IEnumerable<TreeItemData<GroupNodeDto>> items) =>
        items.SelectMany(i => (IEnumerable<TreeItemData<GroupNodeDto>>)[i, .. Flatten(Typed(i.Children ?? []))]);

    private static IEnumerable<TreeItemData<GroupNodeDto>> Typed(IEnumerable<ITreeItemData<GroupNodeDto>> items) => items.OfType<TreeItemData<GroupNodeDto>>();

    private Task ToggleAsync(GroupNodeDto node, bool on)
    {
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

    private Task SelectAsync(GroupNodeDto? node) => node is null || Disabled(node) ? Task.CompletedTask : SelectedChanged.InvokeAsync(node);

    private sealed class GroupNodeComparer : IEqualityComparer<GroupNodeDto?>
    {
        public static readonly GroupNodeComparer Instance = new();

        public bool Equals(GroupNodeDto? x, GroupNodeDto? y) => x?.Id == y?.Id;

        public int GetHashCode(GroupNodeDto? obj) => obj?.Id.GetHashCode() ?? 0;
    }
}
