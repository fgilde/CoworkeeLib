using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Query;

/// <summary>The values of a rule: one, two for a range, a list for "any of", or a number of days.</summary>
public partial class QueryValueEditor
{
    private string? _entry;

    [Parameter, EditorRequired] public QueryRule Rule { get; set; } = null!;

    [Parameter, EditorRequired] public QueryField Field { get; set; } = null!;

    [Parameter] public bool Dense { get; set; } = true;

    [Parameter] public Func<string, string>? Localize { get; set; }

    [Parameter] public EventCallback Changed { get; set; }

    private Margin Margin => Dense ? Margin.Dense : Margin.Normal;

    private int? Days => int.TryParse(Rule.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days) ? days : null;

    private string T(string text) => Localize?.Invoke(text) ?? text;

    private string? Label(string? value) => value is not null && Rule.Labels?.TryGetValue(value, out var label) == true ? label : null;

    private string Joined(IReadOnlyList<string> values) => string.Join(", ", values.Select(v => QueryDescriber.Show(v, Field, Rule)));

    private void Remember(QueryOption option) => (Rule.Labels ??= [])[option.Value] = option.Label;

    private Task ValueAsync(string? value)
    {
        Rule.Value = value;
        return Changed.InvokeAsync();
    }

    private Task Value2Async(string? value)
    {
        Rule.Value2 = value;
        return Changed.InvokeAsync();
    }

    private Task DaysAsync(int? days) => ValueAsync(days?.ToString(CultureInfo.InvariantCulture));

    private Task ValuesAsync(IEnumerable<string>? values)
    {
        Rule.Values = values?.ToList() is { Count: > 0 } list ? list : null;
        return Changed.InvokeAsync();
    }

    private Task AddAsync(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || Rule.Values?.Contains(value) == true)
        {
            return Task.CompletedTask;
        }

        (Rule.Values ??= []).Add(value);
        return Changed.InvokeAsync();
    }

    private Task RemoveAsync(string value)
    {
        Rule.Values?.Remove(value);
        Rule.Values = Rule.Values is { Count: > 0 } ? Rule.Values : null;
        return Changed.InvokeAsync();
    }

    private async Task EntryKeyAsync(KeyboardEventArgs args)
    {
        if (args.Key is "Enter" or ",")
        {
            await EntryBlurAsync();
        }
    }

    private async Task EntryBlurAsync()
    {
        var entry = _entry?.Trim().TrimEnd(',');
        _entry = null;
        if (Field.Type == QueryFieldType.Number)
        {
            entry = decimal.TryParse(entry, NumberStyles.Float, CultureInfo.CurrentCulture, out var number) ? number.ToString(CultureInfo.InvariantCulture) : null;
        }

        await AddAsync(entry);
    }
}
