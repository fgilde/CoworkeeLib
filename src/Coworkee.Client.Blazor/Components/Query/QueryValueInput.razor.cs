using System.Globalization;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Query;

/// <summary>One value of a rule, edited the way its field reads: a choice, a lookup, a number, a date or text. Values stay invariant text.</summary>
public partial class QueryValueInput
{
    [Parameter, EditorRequired] public QueryField Field { get; set; } = null!;

    [Parameter] public string? Value { get; set; }

    [Parameter] public EventCallback<string?> ValueChanged { get; set; }

    /// <summary>The label a lookup shows for <see cref="Value"/>.</summary>
    [Parameter] public string? ValueLabel { get; set; }

    /// <summary>Raised with the picked option of a lookup, so the rule can remember its label.</summary>
    [Parameter] public EventCallback<QueryOption> OnPicked { get; set; }

    [Parameter] public string? Label { get; set; }

    [Parameter] public bool Dense { get; set; } = true;

    private Margin Margin => Dense ? Margin.Dense : Margin.Normal;

    private QueryOption? Selected => Value is null ? null : new QueryOption(Value, ValueLabel ?? Value);

    private decimal? Number => decimal.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;

    private DateTime? Date => QueryDates.Day(Value, TimeSpan.Zero)?.DateTime;

    private Task SetAsync(string? value) => ValueChanged.InvokeAsync(string.IsNullOrEmpty(value) ? null : value);

    private Task NumberAsync(decimal? number) => SetAsync(number?.ToString(CultureInfo.InvariantCulture));

    private Task DateAsync(DateTime? date) => SetAsync(date is { } d ? QueryDates.Format(d) : null);

    private async Task PickAsync(QueryOption? option)
    {
        if (option is not null)
        {
            await OnPicked.InvokeAsync(option);
        }

        await SetAsync(option?.Value);
    }

    private async Task<IEnumerable<QueryOption>> SearchAsync(string? text, CancellationToken cancellationToken) =>
        Field.Lookup is { } lookup ? await lookup(text, cancellationToken) : [];
}
