using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Store = Coworkee.Client.Blazor.ClientEntities.Column;

namespace Coworkee.Client.Blazor.ClientEntities;

/// <summary>
/// The local rows of a set as columns. Rows restored from the browser storage come back as columns and become objects only when
/// shown; filters, sort orders and facet counts work on the columns and are kept until the rows change.
/// </summary>
internal sealed class LocalTable<T>
{
    private const int MaxCached = 64;
    private static readonly byte[] Magic = "CWKC"u8.ToArray();
    private static readonly MethodInfo ExtractValues = typeof(LocalTable<T>).GetMethod(nameof(Extract), BindingFlags.NonPublic | BindingFlags.Static)!;

    private readonly T[] _rows;
    private readonly ConcurrentDictionary<string, Column> _columns;
    private readonly ConcurrentDictionary<string, bool[]> _masks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int[]> _orders = new(StringComparer.Ordinal);
    private int _materialized;

    private LocalTable(T[] rows, ConcurrentDictionary<string, Column> columns, bool materialized)
    {
        _rows = rows;
        _columns = columns;
        _materialized = materialized ? 1 : 0;
    }

    /// <summary>Null when <typeparamref name="T"/> has properties that are not columns; such rows are kept as JSON.</summary>
    public static RowShape? Shape { get; } = RowShape.Of(typeof(T));

    private static string Schema { get; } = $"{typeof(T).FullName}|{Environment.Version}|"
        + string.Join(",", Shape?.Columns.Select(p => p + ":" + TypeOf(p).FullName) ?? []);

    public int Count => _rows.Length;

    public static LocalTable<T> Of(IEnumerable<T> rows) => new([.. rows], new(StringComparer.OrdinalIgnoreCase), materialized: true);

    public T Row(int index) => _rows[index] is { } row ? row : _rows[index] = (T)Shape!.Create(_columns, index);

    public T[] All()
    {
        if (_materialized == 0)
        {
            for (var i = 0; i < _rows.Length; i++)
            {
                Row(i);
            }

            _materialized = 1;
        }

        return _rows;
    }

    public Column Column(string path) => _columns.GetOrAdd(path, Build);

    public IEnumerable<string> Indexable => Shape?.Columns.Where(p => !p.EndsWith(RowShape.Present, StringComparison.Ordinal)) ?? [];

    /// <summary>The rows matching <paramref name="filter"/> in the order of <paramref name="orderBy"/>.</summary>
    public int[] Find(string? filter, string? orderBy)
    {
        var mask = string.IsNullOrWhiteSpace(filter) ? null : Mask(filter);
        var order = string.IsNullOrWhiteSpace(orderBy) ? null : Order(orderBy);
        var found = new List<int>(mask is null ? Count : Count / 4);
        for (var i = 0; i < Count; i++)
        {
            var row = order is null ? i : order[i];
            if (mask is null || mask[row])
            {
                found.Add(row);
            }
        }

        return [.. found];
    }

    public bool[] Mask(string filter) => Cached(_masks, filter, f => LocalFilter.Evaluate(this, ODataLinq.Parse(f)));

    /// <summary>Single orders are the column's index; several combine the ranks of their columns into one key per row.</summary>
    public int[] Order(string orderBy) => Cached(_orders, orderBy, text =>
    {
        var clauses = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(c => c.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Select(p => (Column: Column(p[0].Replace('.', '/')), Descending: p.Length > 1 && p[1].Equals("desc", StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (clauses is [var single])
        {
            return single.Descending ? [.. single.Column.Order.AsEnumerable().Reverse()] : single.Column.Order;
        }

        var keys = new long[Count];
        long span = 1;
        foreach (var (column, descending) in clauses)
        {
            if (span > long.MaxValue / column.Distinct)
            {
                span = Densify(keys);
            }

            var ranks = column.Ranks;
            var distinct = column.Distinct;
            for (var i = 0; i < keys.Length; i++)
            {
                keys[i] = keys[i] * distinct + (descending ? distinct - 1 - ranks[i] : ranks[i]);
            }

            span *= distinct;
        }

        var order = Enumerable.Range(0, Count).ToArray();
        Array.Sort(keys, order);
        return order;
    });

    public void Write(Stream stream, DateTimeOffset version)
    {
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(Schema);
        writer.Write(version.UtcTicks);
        writer.Write(Count);
        foreach (var path in Shape!.Columns)
        {
            Column(path).Write(writer);
        }
    }

    /// <summary>Null when the data was written for another row type; <paramref name="pause"/> runs between columns.</summary>
    public static async Task<(LocalTable<T> Table, DateTimeOffset Version)?> ReadAsync(Stream stream, Func<Task> pause)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (Shape is null || !reader.ReadBytes(Magic.Length).AsSpan().SequenceEqual(Magic) || reader.ReadString() != Schema)
        {
            return null;
        }

        var version = new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero);
        var count = reader.ReadInt32();
        var columns = new ConcurrentDictionary<string, Column>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Shape.Columns)
        {
            columns[path] = Store.Read(TypeOf(path), reader, count);
            await pause();
        }

        return (new LocalTable<T>(new T[count], columns, materialized: false), version);
    }

    public static bool IsColumns(ReadOnlySpan<byte> data) => data.StartsWith(Magic);

    private static Type TypeOf(string path) =>
        path.EndsWith(RowShape.Present, StringComparison.Ordinal) ? typeof(bool) : ODataLinq.Key<T>(path).ReturnType;

    private Column Build(string path)
    {
        var rows = All();
        var present = path.EndsWith(RowShape.Present, StringComparison.Ordinal);
        var key = ODataLinq.Key<T>(present ? path[..^1] : path);
        if (present)
        {
            key = Expression.Lambda(Expression.NotEqual(key.Body, Expression.Constant(null, key.ReturnType)), key.Parameters);
        }

        return Store.Create(key.ReturnType, (Array)ExtractValues.MakeGenericMethod(key.ReturnType).Invoke(null, [rows, key.Compile()])!);
    }

    private static TValue[] Extract<TValue>(T[] rows, Func<T, TValue> getter)
    {
        var values = new TValue[rows.Length];
        for (var i = 0; i < rows.Length; i++)
        {
            values[i] = getter(rows[i]);
        }

        return values;
    }

    // replaces combined keys by their dense rank, so further columns fit into a long
    private static long Densify(long[] keys)
    {
        var order = Enumerable.Range(0, keys.Length).ToArray();
        var sorted = (long[])keys.Clone();
        Array.Sort(sorted, order);
        long rank = -1;
        for (var i = 0; i < sorted.Length; i++)
        {
            if (i == 0 || sorted[i] != sorted[i - 1])
            {
                rank++;
            }

            keys[order[i]] = rank;
        }

        return rank + 1;
    }

    private static TValue Cached<TValue>(ConcurrentDictionary<string, TValue> cache, string key, Func<string, TValue> create)
    {
        if (cache.TryGetValue(key, out var value))
        {
            return value;
        }

        if (cache.Count >= MaxCached)
        {
            // ponytail: every search term is a new filter; dropping the cache is cheaper than tracking use
            cache.Clear();
        }

        return cache[key] = create(key);
    }
}
