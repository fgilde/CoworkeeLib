using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.OData.UriParser;

namespace Coworkee.Client.Blazor.ClientEntities;

/// <summary>
/// The values of one property path over all local rows. Like a database index it sorts them once, on first use: rows get the rank of
/// their value, so filters, sorting and facet counts become loops over integers.
/// </summary>
internal abstract class Column
{
    private static readonly MethodInfo WriteStructs = typeof(Column).GetMethod(nameof(WriteValues), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo ReadStructs = typeof(Column).GetMethod(nameof(ReadValues), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo WriteOptionals = typeof(Column).GetMethod(nameof(WriteOptional), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo ReadOptionals = typeof(Column).GetMethod(nameof(ReadOptional), BindingFlags.NonPublic | BindingFlags.Static)!;

    public abstract Type ValueType { get; }

    /// <summary>Rows in ascending order of their value, nulls first.</summary>
    public abstract int[] Order { get; }

    /// <summary>The rank of each row's value: equal values share a rank, ranks follow the sort order.</summary>
    public abstract int[] Ranks { get; }

    public abstract int Distinct { get; }

    /// <summary>Where each rank begins in <see cref="Order"/>, and the row count at the end.</summary>
    protected abstract int[] Starts { get; }

    public abstract object? Value(int row);

    /// <summary>Which ranks satisfy "value op literal"; the literal has the column's type.</summary>
    public abstract RankSet Compare(BinaryOperatorKind op, object? literal);

    public abstract bool[] In(IEnumerable<object?> literals);

    /// <summary>Which ranks of an enum column have every bit of <paramref name="flag"/>.</summary>
    public abstract bool[] Has(object flag);

    /// <summary>contains, startswith or endswith on the text, first lowered or uppered when <paramref name="casing"/> says so.</summary>
    public abstract bool[] Text(string function, string? casing, string literal);

    public abstract int RankOf(object? value);

    public abstract void Write(BinaryWriter writer);

    /// <summary>Builds the index; true when it had to sort, false when it came with the column.</summary>
    public abstract bool Index();

    /// <summary>The rows whose rank is set; rows of one rank lie side by side in <see cref="Order"/>, so only matching rows are touched.</summary>
    public bool[] Rows(bool[] byRank)
    {
        var order = Order;
        var starts = Starts;
        var rows = new bool[order.Length];
        for (var rank = 0; rank < byRank.Length; rank++)
        {
            if (byRank[rank])
            {
                for (var at = starts[rank]; at < starts[rank + 1]; at++)
                {
                    rows[order[at]] = true;
                }
            }
        }

        return rows;
    }

    public bool[] Rows(RankSet ranks)
    {
        if (ranks.Set is { } set)
        {
            return Rows(set);
        }

        var order = Order;
        var starts = Starts;
        var rows = new bool[order.Length];
        for (var at = starts[ranks.From]; at < starts[ranks.To]; at++)
        {
            rows[order[at]] = true;
        }

        return rows;
    }

    /// <summary>How many of <paramref name="rows"/> lie below each rank: entry r is the count of ranks 0..r-1, the last one all of them.</summary>
    public int[] Prefix(int[] rows)
    {
        var ranks = Ranks;
        var prefix = new int[Distinct + 1];
        foreach (var row in rows)
        {
            prefix[ranks[row] + 1]++;
        }

        for (var rank = 1; rank < prefix.Length; rank++)
        {
            prefix[rank] += prefix[rank - 1];
        }

        return prefix;
    }

    public static Column Create(Type type, Array values, int[]? order = null) =>
        (Column)Activator.CreateInstance(typeof(Column<>).MakeGenericType(type), values, order)!;

    /// <summary>The values, and the sort order when it was written with them, so a restored copy needs no sorting.</summary>
    public static Column Read(Type type, BinaryReader reader, int count)
    {
        var values = type == typeof(string)
            ? ReadTexts(reader, count)
            : Nullable.GetUnderlyingType(type) is { } plain
                ? (Array)ReadOptionals.MakeGenericMethod(plain).Invoke(null, [reader, count])!
                : (Array)ReadStructs.MakeGenericMethod(type).Invoke(null, [reader, count])!;
        return Create(type, values, reader.ReadBoolean() ? ReadValues<int>(reader, count) : null);
    }

    /// <summary>Value types without references are copied as memory; text as lengths and characters.</summary>
    public static bool Supports(Type type)
    {
        var plain = Nullable.GetUnderlyingType(type) ?? type;
        return type == typeof(string) || plain.IsEnum || plain.IsPrimitive || plain == typeof(decimal) || plain == typeof(Guid) || plain == typeof(DateTime)
            || plain == typeof(DateTimeOffset) || plain == typeof(DateOnly) || plain == typeof(TimeOnly) || plain == typeof(TimeSpan);
    }

    protected static void WriteArray(BinaryWriter writer, Array values)
    {
        if (values is string?[] texts)
        {
            WriteTexts(writer, texts);
        }
        else if (Nullable.GetUnderlyingType(values.GetType().GetElementType()!) is { } plain)
        {
            WriteOptionals.MakeGenericMethod(plain).Invoke(null, [writer, values]);
        }
        else
        {
            WriteStructs.MakeGenericMethod(values.GetType().GetElementType()!).Invoke(null, [writer, values]);
        }
    }

    protected static void WriteValues<TValue>(BinaryWriter writer, TValue[] values)
        where TValue : struct => writer.Write(MemoryMarshal.AsBytes(values.AsSpan()));

    private static TValue[] ReadValues<TValue>(BinaryReader reader, int count)
        where TValue : struct
    {
        var values = new TValue[count];
        reader.BaseStream.ReadExactly(MemoryMarshal.AsBytes(values.AsSpan()));
        return values;
    }

    // nullable values as two arrays: whether there is one, and the value
    private static void WriteOptional<TValue>(BinaryWriter writer, TValue?[] values)
        where TValue : struct
    {
        WriteValues(writer, values.Select(v => v.HasValue).ToArray());
        WriteValues(writer, values.Select(v => v.GetValueOrDefault()).ToArray());
    }

    private static TValue?[] ReadOptional<TValue>(BinaryReader reader, int count)
        where TValue : struct
    {
        var present = ReadValues<bool>(reader, count);
        var values = ReadValues<TValue>(reader, count);
        var optional = new TValue?[count];
        for (var i = 0; i < count; i++)
        {
            optional[i] = present[i] ? values[i] : null;
        }

        return optional;
    }

    private static void WriteTexts(BinaryWriter writer, string?[] texts)
    {
        var lengths = new int[texts.Length];
        var total = 0;
        for (var i = 0; i < texts.Length; i++)
        {
            lengths[i] = texts[i]?.Length ?? -1;
            total += Math.Max(lengths[i], 0);
        }

        var chars = new char[total];
        var at = 0;
        foreach (var text in texts)
        {
            text?.CopyTo(chars.AsSpan(at));
            at += text?.Length ?? 0;
        }

        writer.Write(total);
        writer.Write(MemoryMarshal.AsBytes(lengths.AsSpan()));
        writer.Write(MemoryMarshal.AsBytes(chars.AsSpan()));
    }

    private static string?[] ReadTexts(BinaryReader reader, int count)
    {
        var chars = new char[reader.ReadInt32()];
        var lengths = new int[count];
        reader.BaseStream.ReadExactly(MemoryMarshal.AsBytes(lengths.AsSpan()));
        reader.BaseStream.ReadExactly(MemoryMarshal.AsBytes(chars.AsSpan()));
        var texts = new string?[count];
        var at = 0;
        for (var i = 0; i < count; i++)
        {
            if (lengths[i] >= 0)
            {
                texts[i] = new string(chars, at, lengths[i]);
                at += lengths[i];
            }
        }

        return texts;
    }
}

internal sealed class Column<TValue>(TValue[] values, int[]? order) : Column
{
    private static readonly IComparer<TValue> Sorter = typeof(TValue) == typeof(string) ? (IComparer<TValue>)(object)TextComparer.Instance : Comparer<TValue>.Default;
    private static readonly EqualityComparer<TValue> Same = EqualityComparer<TValue>.Default;

    private Sorted? _sorted;
    private string?[]? _lower;
    private string?[]? _upper;

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

        // contains(tolower(Path),'abc') matches like an ignore-case search, without lowering every value
        var ignoreCase = (casing == "tolower" && literal == literal.ToLowerInvariant()) || (casing == "toupper" && literal == literal.ToUpperInvariant());
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var texts = casing switch
        {
            _ when ignoreCase => distinct,
            "tolower" => _lower ??= [.. distinct.Select(t => t?.ToLowerInvariant())],
            "toupper" => _upper ??= [.. distinct.Select(t => t?.ToUpperInvariant())],
            _ => distinct,
        };
        var result = new bool[texts.Length];
        for (var rank = 0; rank < texts.Length; rank++)
        {
            // OData string functions read null as empty text
            var text = texts[rank] ?? string.Empty;
            result[rank] = function switch
            {
                "contains" => text.Contains(literal, comparison),
                "startswith" => text.StartsWith(literal, comparison),
                _ => text.EndsWith(literal, comparison),
            };
        }

        return result;
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
        writer.Write(_sorted is not null);
        if (_sorted is { } sorted)
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
}

/// <summary>Text sorts ignoring case like the server; values that differ only in case stay apart, so equality remains exact.</summary>
internal sealed class TextComparer : IComparer<string?>
{
    public static readonly TextComparer Instance = new();

    private static readonly StringComparer Culture = StringComparer.CurrentCultureIgnoreCase;

    public int Compare(string? x, string? y) => Culture.Compare(x, y) is var order and not 0 ? order : string.CompareOrdinal(x, y);
}
