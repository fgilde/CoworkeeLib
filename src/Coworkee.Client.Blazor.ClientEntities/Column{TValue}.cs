using System.Globalization;
using Microsoft.OData.UriParser;

namespace Coworkee.Client.Blazor.ClientEntities;

internal sealed class Column<TValue>(TValue[] values, int[]? order) : Column
{
    private static readonly IComparer<TValue> Sorter = typeof(TValue) == typeof(string) ? (IComparer<TValue>)(object)TextComparer.Instance : Comparer<TValue>.Default;
    private static readonly EqualityComparer<TValue> Same = EqualityComparer<TValue>.Default;

    private Sorted? _sorted;
    private string?[]? _lower;
    private string?[]? _upper;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Joined> _joined = new();

    public override Type ValueType => typeof(TValue);

    public override int[] Order => Indexed.Order;

    public override int[] Ranks => Indexed.Ranks;

    public override int Distinct => Indexed.Values.Length;

    protected override int[] Starts => Indexed.Starts;

    private Sorted Indexed => _sorted ?? Build();

    // nulls sort first, so a null value always has rank 0
    private bool HasNull => Indexed.Values is [null, ..];

    public override object? Value(int row) => values[row];

    public override bool Index()
    {
        var sorts = _sorted is null && order is null;
        _ = Indexed;
        return sorts;
    }

    public override RankSet Compare(BinaryOperatorKind op, object? literal)
    {
        var distinct = Indexed.Values;
        var first = HasNull ? 1 : 0;
        if (literal is null)
        {
            return op switch
            {
                BinaryOperatorKind.Equal => RankSet.Range(0, first),
                BinaryOperatorKind.NotEqual => RankSet.Range(first, distinct.Length),
                _ => RankSet.Range(0, 0),
            };
        }

        var at = Array.BinarySearch(distinct, first, distinct.Length - first, (TValue)literal, Sorter);
        var from = at >= 0 ? at : ~at;
        var above = at >= 0 ? at + 1 : ~at;
        return op switch
        {
            BinaryOperatorKind.Equal => RankSet.Range(from, above),
            BinaryOperatorKind.NotEqual => RankSet.Range(from, above).Not(distinct.Length),
            BinaryOperatorKind.GreaterThan => RankSet.Range(above, distinct.Length),
            BinaryOperatorKind.GreaterThanOrEqual => RankSet.Range(from, distinct.Length),
            BinaryOperatorKind.LessThan => RankSet.Range(first, from),
            BinaryOperatorKind.LessThanOrEqual => RankSet.Range(first, above),
            _ => throw new NotSupportedException($"{op} is not supported on a column."),
        };
    }

    public override bool[] In(IEnumerable<object?> literals)
    {
        var result = new bool[Distinct];
        foreach (var literal in literals)
        {
            if (RankOf(literal) is var rank and >= 0)
            {
                result[rank] = true;
            }
        }

        return result;
    }

    public override bool[] Has(object flag)
    {
        if (!(Nullable.GetUnderlyingType(typeof(TValue)) ?? typeof(TValue)).IsEnum)
        {
            throw new NotSupportedException("'has' needs an enum property and a flag.");
        }

        var bits = Convert.ToInt64(flag, CultureInfo.InvariantCulture);
        var distinct = Indexed.Values;
        var result = new bool[distinct.Length];
        for (var rank = 0; rank < result.Length; rank++)
        {
            result[rank] = distinct[rank] is { } value && (Convert.ToInt64(value, CultureInfo.InvariantCulture) & bits) == bits;
        }

        return result;
    }

    public override bool[] Text(string function, string? casing, string literal)
    {
        if (Indexed.Values is not string?[] distinct)
        {
            throw new NotSupportedException("String functions need text.");
        }

        var texts = casing switch
        {
            "tolower" => _lower ??= [.. distinct.Select(t => t?.ToLowerInvariant())],
            "toupper" => _upper ??= [.. distinct.Select(t => t?.ToUpperInvariant())],
            _ => distinct,
        };
        if (function == "contains")
        {
            return Contains(_joined.GetOrAdd(casing ?? string.Empty, _ => Join(texts)), literal);
        }

        var result = new bool[texts.Length];
        for (var rank = 0; rank < texts.Length; rank++)
        {
            // OData string functions read null as empty text
            var text = texts[rank] ?? string.Empty;
            result[rank] = function == "startswith" ? text.StartsWith(literal, StringComparison.Ordinal) : text.EndsWith(literal, StringComparison.Ordinal);
        }

        return result;
    }

    // one search through all values joined by '\0' instead of a call per value: WebAssembly interprets each call, the search itself runs vectorized
    private static bool[] Contains(Joined joined, string literal)
    {
        var starts = joined.Starts;
        var result = new bool[starts.Length - 1];
        if (literal.Length == 0)
        {
            Array.Fill(result, true);
            return result;
        }

        for (var at = joined.Text.IndexOf(literal, StringComparison.Ordinal); at >= 0;)
        {
            var rank = Array.BinarySearch(starts, at);
            rank = rank >= 0 ? rank : ~rank - 1;
            // a match across the separator into the next value does not count
            if (at + literal.Length < starts[rank + 1])
            {
                result[rank] = true;
                at = starts[rank + 1];
            }
            else
            {
                at++;
            }

            at = at < joined.Text.Length ? joined.Text.IndexOf(literal, at, StringComparison.Ordinal) : -1;
        }

        return result;
    }

    private static Joined Join(string?[] texts)
    {
        var starts = new int[texts.Length + 1];
        var builder = new System.Text.StringBuilder();
        for (var rank = 0; rank < texts.Length; rank++)
        {
            starts[rank] = builder.Length;
            builder.Append(texts[rank]).Append('\0');
        }

        starts[^1] = builder.Length;
        return new Joined(builder.ToString(), starts);
    }

    public override int RankOf(object? value)
    {
        if (value is null)
        {
            return HasNull ? 0 : -1;
        }

        var distinct = Indexed.Values;
        var first = HasNull ? 1 : 0;
        return Math.Max(Array.BinarySearch(distinct, first, distinct.Length - first, (TValue)value, Sorter), -1);
    }

    public override void Write(BinaryWriter writer)
    {
        WriteArray(writer, values);
        // read once: the column may get sorted meanwhile (indexing runs beside the saving), the flag and the order must agree
        var sorted = _sorted;
        writer.Write(sorted is not null);
        if (sorted is not null)
        {
            WriteValues(writer, sorted.Order);
        }
    }

    private Sorted Build()
    {
        var sorted = order ?? Sort();
        var ranks = new int[sorted.Length];
        var distinct = new List<TValue>();
        var starts = new List<int>();
        for (var i = 0; i < sorted.Length; i++)
        {
            var value = values[sorted[i]];
            if (i == 0 || !Same.Equals(value, distinct[^1]))
            {
                distinct.Add(value);
                starts.Add(i);
            }

            ranks[sorted[i]] = distinct.Count - 1;
        }

        starts.Add(sorted.Length);
        Interlocked.CompareExchange(ref _sorted, new Sorted(sorted, ranks, [.. distinct], [.. starts]), null);
        return _sorted!;
    }

    private int[] Sort()
    {
        var keys = (TValue[])values.Clone();
        var sorted = new int[keys.Length];
        for (var i = 0; i < sorted.Length; i++)
        {
            sorted[i] = i;
        }

        Array.Sort(keys, sorted, Sorter);
        return sorted;
    }

    private sealed record Sorted(int[] Order, int[] Ranks, TValue[] Values, int[] Starts);

    private sealed record Joined(string Text, int[] Starts);
}
