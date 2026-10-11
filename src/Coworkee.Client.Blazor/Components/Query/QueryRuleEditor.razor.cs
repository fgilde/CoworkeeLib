using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Query;

/// <summary>One rule of the query builder: field, operator and the value editor that fits both.</summary>
public partial class QueryRuleEditor
{
    [Parameter, EditorRequired] public QueryRule Rule { get; set; } = null!;

    [Parameter, EditorRequired] public IReadOnlyList<QueryField> Fields { get; set; } = [];

    [Parameter] public bool Dense { get; set; } = true;

    [Parameter] public Func<string, string>? Localize { get; set; }

    [Parameter] public EventCallback Changed { get; set; }

    [Parameter] public EventCallback OnRemove { get; set; }

    [Parameter] public (int Index, int Count) Position { get; set; }

    [Parameter] public EventCallback<int> OnMove { get; set; }

    private QueryField? Field => Fields.FirstOrDefault(f => string.Equals(f.Name, Rule.Field, StringComparison.OrdinalIgnoreCase));

    private Margin Margin => Dense ? Margin.Dense : Margin.Normal;

    private string T(string text) => Localize?.Invoke(text) ?? text;

    private static string Icon(QueryField field) => field.Type switch
    {
        QueryFieldType.Number => Icons.Material.Outlined.Numbers,
        QueryFieldType.Boolean => Icons.Material.Outlined.ToggleOn,
        QueryFieldType.Date or QueryFieldType.DateTime => Icons.Material.Outlined.Event,
        QueryFieldType.Enum => Icons.Material.Outlined.List,
        _ when field.HasChoices => Icons.Material.Outlined.Link,
        _ => Icons.Material.Outlined.TextFields,
    };

    // a new field keeps the operator when it fits, values never: "Total > 5" means nothing for a date
    private Task FieldAsync(string field)
    {
        var target = Fields.First(f => f.Name == field);
        var operators = QueryOperators.For(target);
        Rule.Operator = Field is { } old && old.Type == target.Type && operators.Contains(Rule.Operator) ? Rule.Operator : operators[0];
        Rule.Field = field;
        (Rule.Value, Rule.Value2, Rule.Values, Rule.Labels) = (null, null, null, null);
        return Changed.InvokeAsync();
    }

    private Task OperatorAsync(QueryOperator op)
    {
        var listed = QueryOperators.IsList(op);
        if (listed && !QueryOperators.IsList(Rule.Operator) && !string.IsNullOrEmpty(Rule.Value))
        {
            Rule.Values = [Rule.Value];
        }
        else if (!listed && QueryOperators.IsList(Rule.Operator))
        {
            Rule.Value = Rule.Values?.FirstOrDefault();
        }

        if (!listed)
        {
            Rule.Values = null;
        }

        var days = op is QueryOperator.InLast or QueryOperator.InNext;
        if (days != (Rule.Operator is QueryOperator.InLast or QueryOperator.InNext))
        {
            Rule.Value = days ? "7" : null;
        }

        Rule.Operator = op;
        Rule.Value2 = QueryOperators.IsRange(op) ? Rule.Value2 : null;
        return Changed.InvokeAsync();
    }
}
