using System.Globalization;
using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Extensions.Helper;
using Nextended.Core.Contracts;
using Nextended.Core.Types;
using Op = MudBlazor.FilterOperator;

namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>
/// The filter of one column, opened from its header: an operator and a value for text, numbers and dates, check boxes for enums and yes/no,
/// or the options of the column's facet with their counts, so a facet and its column filter are the same choice.
/// </summary>
public partial class DataTableColumnFilter<T>
{
    private string _operator = string.Empty;
    private object? _value;
    private object? _to;
    private HashSet<object> _chosen = [];
    private string? _loaded;
    private (object? Min, object? Max)? _bounds;
    private Column<T>? _boundsOf;

    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public FilterContext<T> Context { get; set; } = null!;

    /// <summary>The facet of the column; its options replace the editor.</summary>
    [Parameter] public FacetGroupDto? Facet { get; set; }

    [Parameter, EditorRequired] public FacetSelection Selection { get; set; } = null!;

    [Parameter] public IReadOnlyList<string> Operators { get; set; } = [];

    /// <summary>The smallest and largest value of the column, asked once "between" is chosen, for the range slider.</summary>
    [Parameter] public Func<Task<(object? Min, object? Max)>>? LoadBounds { get; set; }


    private IRange<decimal>? NumberBounds =>
        _bounds is { Min: { } min, Max: { } max } && ToDecimal(min) < ToDecimal(max) ? new MudExRange<decimal>(ToDecimal(min), ToDecimal(max)) : null;

    private IRange<DateOnly>? DateBounds =>
        _bounds is { Min: { } min, Max: { } max } && ToDate(min) is { } from && ToDate(max) is { } to && from.Date < to.Date
            ? new MudExRange<DateOnly>(DateOnly.FromDateTime(from), DateOnly.FromDateTime(to))
            : null;

    private Column<T> Column => Context.FilterDefinition!.Column!;

    private Type Type => DataGridLayout<T>.ValueType(Column) ?? typeof(string);

    private FieldType Field => FieldType.Identify(Type);

    private bool IsDate => DataTableFilters.IsDate(Type);

    private bool IsBetween => _operator == DataTableFilters.Between;

    private bool NeedsValue => _operator is not (Op.String.Empty or Op.String.NotEmpty);

    private IReadOnlyList<object>? Choices => Field.IsEnum ? Enum.GetValues(Type).Cast<object>().ToList() : Field.IsBoolean ? [true, false] : null;

    private List<IFilterDefinition<T>> Current => Context.FilterDefinitions.Where(d => d.Column == Column && d.Operator != DataTableFilters.FacetMarker).ToList();

    private string? Text
    {
        get => _value?.ToString();
        set => _value = value;
    }

    private double? Number
    {
        get => ToNumber(_value);
        set => _value = value;
    }

    private double? NumberTo
    {
        get => ToNumber(_to);
        set => _to = value;
    }

    private DateTime? Date
    {
        get => ToDate(_value);
        set => _value = value;
    }

    private DateTime? DateTo
    {
        get => ToDate(_to);
        set => _to = value;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // the grid reuses one filter for every column
        if (IsBetween && (Field.IsNumber || IsDate) && _boundsOf != Column && LoadBounds is not null)
        {
            (_boundsOf, _bounds) = (Column, null);
            try
            {
                _bounds = await LoadBounds();
            }
            catch (Exception exception) when (exception is Api.ApiException or HttpRequestException)
            {
                // the fields still work without the slider
            }

            StateHasChanged();
        }
    }

    protected override void OnParametersSet()
    {
        var current = Current;
        var loaded = string.Join(",", current.Select(d => d.Id));
        if (loaded != _loaded)
        {
            _loaded = loaded;
            Load(current);
        }
    }

    private void Load(IReadOnlyList<IFilterDefinition<T>> current)
    {
        var (from, to) = DataTableFilters.Range(Type);
        _chosen = current.Where(d => d.Value is not null).Select(d => d.Value!).ToHashSet();
        if (current.Count > 0 && Operators.Contains(DataTableFilters.Between) && current.All(d => d.Operator == from || d.Operator == to))
        {
            _operator = DataTableFilters.Between;
            (_value, _to) = (current.FirstOrDefault(d => d.Operator == from)?.Value, current.FirstOrDefault(d => d.Operator == to)?.Value);
            return;
        }

        _operator = current.FirstOrDefault()?.Operator ?? Operators.FirstOrDefault() ?? string.Empty;
        (_value, _to) = (current.FirstOrDefault()?.Value, null);
    }

    private void Choose(object choice, bool chosen)
    {
        if (chosen)
        {
            _chosen.Add(choice);
        }
        else
        {
            _chosen.Remove(choice);
        }
    }

    private string ChoiceLabel(object choice) => choice switch
    {
        bool flag => L[flag ? "Yes" : "No"],
        Enum value => L[EnumLabels.Of(value)],
        _ => L[choice.ToString()!],
    };

    private Task ApplyAsync()
    {
        var (from, to) = DataTableFilters.Range(Type);
        IEnumerable<(string Operator, object? Value)> parts = Choices is not null ? _chosen.Select(c => (Op.Enum.Is, (object?)c))
            : IsBetween ? [(from, Coerce(_value)), (to, Coerce(_to))]
            : [(_operator, NeedsValue ? Coerce(_value) : null)];
        var filters = parts.Where(p => p.Value is not null || p.Operator is Op.String.Empty or Op.String.NotEmpty)
            .Select(p => (IFilterDefinition<T>)DataGridLayout<T>.Definition(Column, p.Operator, p.Value)).ToList();
        Context.FilterDefinitions.RemoveAll(d => d.Column == Column && d.Operator != DataTableFilters.FacetMarker);
        return Context.Actions.ApplyFiltersAsync(filters);
    }

    private Task ClearAsync()
    {
        (_value, _to) = (null, null);
        _chosen.Clear();
        Context.FilterDefinitions.RemoveAll(d => d.Column == Column && d.Operator != DataTableFilters.FacetMarker);
        return Context.Actions.ApplyFiltersAsync([]);
    }

    private object? Coerce(object? value) => value switch
    {
        null or "" => null,
        string text => DataTableFilters.Parse(text.Trim(), Type),
        DateTime date when Type == typeof(DateOnly) => DateOnly.FromDateTime(date),
        DateTime date when Type == typeof(DateTimeOffset) => new DateTimeOffset(date),
        double number when Type != typeof(double) => Convert.ChangeType(number, Type, CultureInfo.InvariantCulture),
        _ => value,
    };

    private IRange<decimal> NumberSpan(IRange<decimal> whole) =>
        new MudExRange<decimal>(Number is { } from ? (decimal)from : whole.Start, NumberTo is { } to ? (decimal)to : whole.End);

    private static RangeLength<decimal> NumberStep(IRange<decimal> whole) =>
        new(decimal.Truncate(whole.Start) == whole.Start && decimal.Truncate(whole.End) == whole.End && whole.End - whole.Start >= 20
            ? 1
            : Math.Max(0.01m, Math.Round((whole.End - whole.Start) / 100, 2)));

    private void SlideNumbers(IRange<decimal> range) => (_value, _to) = ((double)Math.Round(range.Start, 2), (double)Math.Round(range.End, 2));

    private IRange<DateOnly> DateSpan(IRange<DateOnly> whole) =>
        new MudExRange<DateOnly>(Date is { } from ? DateOnly.FromDateTime(from) : whole.Start, DateTo is { } to ? DateOnly.FromDateTime(to) : whole.End);

    private void SlideDates(IRange<DateOnly> range) => (_value, _to) = (range.Start.ToDateTime(TimeOnly.MinValue), range.End.ToDateTime(TimeOnly.MinValue));

    private static decimal ToDecimal(object value) => Convert.ToDecimal(value, CultureInfo.InvariantCulture);

    private static double? ToNumber(object? value) => value is null ? null : Convert.ToDouble(value, CultureInfo.InvariantCulture);

    private static DateTime? ToDate(object? value) => value switch
    {
        DateTime date => date,
        DateTimeOffset offset => offset.LocalDateTime,
        DateOnly date => date.ToDateTime(TimeOnly.MinValue),
        _ => null,
    };
}
