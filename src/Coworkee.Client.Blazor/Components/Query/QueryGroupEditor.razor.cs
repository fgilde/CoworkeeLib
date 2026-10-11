using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components.Query;

/// <summary>One group of the query builder: its join, negation, children and the buttons that add rules and nested groups.</summary>
public partial class QueryGroupEditor
{
    private static readonly string[] Accents = ["var(--mud-palette-primary)", "var(--mud-palette-secondary)", "var(--mud-palette-tertiary)", "var(--mud-palette-info)"];

    [Parameter, EditorRequired] public QueryGroup Group { get; set; } = null!;

    [Parameter, EditorRequired] public IReadOnlyList<QueryField> Fields { get; set; } = [];

    [Parameter] public int Depth { get; set; }

    [Parameter] public int MaxDepth { get; set; } = 3;

    [Parameter] public bool Dense { get; set; } = true;

    [Parameter] public Func<string, string>? Localize { get; set; }

    [Parameter] public EventCallback Changed { get; set; }

    [Parameter] public EventCallback OnRemove { get; set; }

    /// <summary>The index among its siblings and their count, for the move buttons.</summary>
    [Parameter] public (int Index, int Count) Position { get; set; }

    [Parameter] public EventCallback<int> OnMove { get; set; }

    private string Class => Depth == 0 ? "qb-group" : "qb-group qb-nested";

    private string Accent => $"--qb-accent: {Accents[Depth % Accents.Length]}";

    private string T(string text) => Localize?.Invoke(text) ?? text;

    private Task LogicAsync(QueryLogic logic)
    {
        Group.Logic = logic;
        return Changed.InvokeAsync();
    }

    private Task NotAsync(bool not)
    {
        Group.Not = not;
        return Changed.InvokeAsync();
    }

    private Task AddRuleAsync()
    {
        var field = Fields.FirstOrDefault();
        Group.Children.Add(new QueryRule { Field = field?.Name, Operator = field is null ? QueryOperator.Equal : QueryOperators.For(field)[0] });
        return Changed.InvokeAsync();
    }

    private Task AddGroupAsync()
    {
        var group = new QueryGroup { Logic = Group.Logic == QueryLogic.And ? QueryLogic.Or : QueryLogic.And };
        var field = Fields.FirstOrDefault();
        group.Children.Add(new QueryRule { Field = field?.Name, Operator = field is null ? QueryOperator.Equal : QueryOperators.For(field)[0] });
        Group.Children.Add(group);
        return Changed.InvokeAsync();
    }

    private Task RemoveAsync(int index)
    {
        Group.Children.RemoveAt(index);
        return Changed.InvokeAsync();
    }

    private Task MoveAsync(int index, int by)
    {
        var target = index + by;
        if (target < 0 || target >= Group.Children.Count)
        {
            return Task.CompletedTask;
        }

        (Group.Children[index], Group.Children[target]) = (Group.Children[target], Group.Children[index]);
        return Changed.InvokeAsync();
    }
}
