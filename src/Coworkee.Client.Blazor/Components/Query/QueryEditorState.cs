using Microsoft.AspNetCore.Components;

using Coworkee.Contracts.Query;

namespace Coworkee.Client.Blazor.Components.Query;

/// <summary>What the editors of one builder share: the root, the depth limit, the selected rules and the node being dragged.</summary>
public sealed class QueryEditorState
{
    public QueryGroup Root { get; set; } = new();

    public int MaxDepth { get; set; } = 3;

    public EventCallback Changed { get; set; }

    public HashSet<string> Selected { get; } = [];

    /// <summary>Renders the whole builder again, for a change that is not a change of the query (the selection).</summary>
    public Action? Rerender { get; set; }

    public QueryNode? Dragged { get; set; }

    /// <summary>Applies a structural edit and tells the builder when it did something.</summary>
    public async Task EditAsync(Func<bool> edit)
    {
        Dragged = null;
        if (edit())
        {
            Selected.Clear();
            await Changed.InvokeAsync();
        }
    }

    public Task DropAsync(QueryGroup target, int? index) =>
        Dragged is { } node ? EditAsync(() => QueryTree.Move(Root, node, target, MaxDepth, index)) : Task.CompletedTask;
}
