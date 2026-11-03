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
