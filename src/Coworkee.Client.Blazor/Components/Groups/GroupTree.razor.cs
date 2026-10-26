using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Groups;

/// <summary>
/// The group tree, loaded level by level as nodes open (MudBlazor's <c>ServerData</c>); a search shows the matches with their ancestors.
/// Used by the groups page and by the group picker dialog.
/// </summary>
public partial class GroupTree
{
    private List<TreeItemData<GroupNodeDto>> _items = [];
    private string _search = string.Empty;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter] public Guid? SelectedId { get; set; }

    [Parameter] public EventCallback<GroupNodeDto> SelectedChanged { get; set; }

    /// <summary>Opens the tree down to the group with this code on the first load.</summary>
    [Parameter] public string? RevealCode { get; set; }

    /// <summary>A group left out with its subtree, e.g. the one being moved.</summary>
    [Parameter] public Guid? Exclude { get; set; }

    [Parameter] public RenderFragment<GroupNodeDto>? NodeActions { get; set; }

    private GroupNodeDto? Selected => Flatten(_items).FirstOrDefault(i => i.Value!.Id == SelectedId)?.Value;

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
        await LoadAsync();
        if (_search.Length == 0)
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

    private async Task LoadAsync() =>
        _items = _search.Length == 0 ? await ChildrenOfAsync(null) : Tree(await Api.SearchGroupTreeAsync(_search));

    private async Task SearchAsync(string? text)
    {
        _search = text?.Trim() ?? string.Empty;
        await LoadAsync();
    }

    private async Task<IReadOnlyCollection<TreeItemData<GroupNodeDto>>> ChildrenAsync(GroupNodeDto? parent) => await ChildrenOfAsync(parent?.Id);

    private async Task<List<TreeItemData<GroupNodeDto>>> ChildrenOfAsync(Guid? parentId) =>
        [.. (await Api.GetGroupChildrenAsync(parentId)).Where(Shown).Select(Item)];

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
        var excluded = nodes.FirstOrDefault(n => n.Id == Exclude)?.Code;
        var items = nodes.Where(n => excluded is null || !GroupPaths.Prefixes(n.Code).Contains(excluded)).ToDictionary(n => n.Id, Item);
        var children = items.Values.ToLookup(i => i.Value!.ParentId);
        foreach (var parent in items.Values.Where(p => children.Contains(p.Value!.Id)))
        {
            (parent.Children, parent.Expanded) = (children[parent.Value!.Id].OrderBy(i => i.Value!.Name).ToList(), true);
        }

        return [.. items.Values.Where(i => i.Value!.ParentId is not { } parentId || !items.ContainsKey(parentId)).OrderBy(i => i.Value!.Name)];
    }

    private bool Shown(GroupNodeDto node) => node.Id != Exclude;

    private static TreeItemData<GroupNodeDto> Item(GroupNodeDto node) => new() { Value = node, Text = node.Name, Expandable = node.ChildCount > 0 };

    private static IEnumerable<TreeItemData<GroupNodeDto>> Flatten(IEnumerable<TreeItemData<GroupNodeDto>> items) =>
        items.SelectMany(i => (IEnumerable<TreeItemData<GroupNodeDto>>)[i, .. Flatten(Typed(i.Children ?? []))]);

    private static IEnumerable<TreeItemData<GroupNodeDto>> Typed(IEnumerable<ITreeItemData<GroupNodeDto>> items) => items.OfType<TreeItemData<GroupNodeDto>>();

    private Task SelectAsync(GroupNodeDto? node) => node is null ? Task.CompletedTask : SelectedChanged.InvokeAsync(node);

    private sealed class GroupNodeComparer : IEqualityComparer<GroupNodeDto?>
    {
        public static readonly GroupNodeComparer Instance = new();

        public bool Equals(GroupNodeDto? x, GroupNodeDto? y) => x?.Id == y?.Id;

        public int GetHashCode(GroupNodeDto? obj) => obj?.Id.GetHashCode() ?? 0;
    }
}
