using Microsoft.AspNetCore.Components;

using Coworkee.Contracts.Query;

namespace Coworkee.Client.Blazor.Components.Query;

/// <summary>The actions of a rule or group: drag handle, up and down, and a menu to move it into or out of a group, wrap it and unwrap it.</summary>
public partial class QueryNodeMenu
{
    [CascadingParameter] public QueryEditorState? State { get; set; }

    [Parameter, EditorRequired] public QueryNode Node { get; set; } = null!;

    [Parameter] public (int Index, int Count) Position { get; set; }

    [Parameter] public EventCallback<int> OnMove { get; set; }

    [Parameter] public Func<string, string>? Localize { get; set; }

    private string T(string text) => Localize?.Invoke(text) ?? text;

    private string Label(QueryGroup group) => QueryTree.GroupLabel(State!.Root, group, T);

    /// <summary>The selected siblings when this node is one of them, else only this node.</summary>
    private List<QueryNode> WrapNodes()
    {
        var parent = QueryTree.ParentOf(State!.Root, Node);
        return State.Selected.Contains(Node.Id) && parent is not null
            ? parent.Children.Where(c => State.Selected.Contains(c.Id)).ToList()
            : [Node];
    }
}
