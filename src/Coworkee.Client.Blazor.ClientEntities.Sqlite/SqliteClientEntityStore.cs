using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.OData.UriParser;

namespace Coworkee.Client.Blazor.ClientEntities.Sqlite;

/// <summary>
/// One copy in a SQLite file of the browser's in-memory file system, kept between visits as the file's bytes in <see cref="IClientEntityStorage"/>:
/// a reload opens the file instead of parsing every row. Pages, counts and filters are SQL through EF Core; facet options on one column count
/// one GROUP BY of that column, others one COUNT each.
/// </summary>
internal sealed class SqliteClientEntityStore<T> : IClientEntityStore<T>
    where T : class
{
    private const string AllRows = "\u0001";
    private static readonly ConcurrentDictionary<string, Func<T, bool>> Predicates = new(StringComparer.Ordinal);
    private static readonly MethodInfo CountsMethod = typeof(SqliteClientEntityStore<T>).GetMethod(nameof(CountsAsync), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo OrderMethod = typeof(SqliteClientEntityStore<T>).GetMethod(nameof(Order), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly string Schema = "1|" + typeof(T).FullName + "|"
        + string.Join(",", CopyModel.Properties(typeof(T)).Select(p => p.Name + " " + CopyModel.Kind(p.PropertyType) + " " + p.PropertyType.Name));

    private readonly string _directory;
    private readonly string _key;
    private readonly string _path;
    private readonly IClientEntityStorage _storage;
    private readonly ILogger _logger;

    // one statement at a time on the connection: a query waits for a write in progress
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SqliteConnection? _connection;
    private DbContextOptions<CopyDb<T>>? _options;

    // directory: the browser's in-memory file system; tests pass a temporary folder
    public SqliteClientEntityStore(string key, IClientEntityStorage storage, ILogger logger, string? directory)
    {
        _directory = directory ?? "/coworkee-copies";
        // the columns in the key: a changed row type starts a new copy instead of reading a file without its columns
        _key = key + "@sqlite:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Schema)))[..12];
        _path = $"{_directory}/{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16]}.db";
        _storage = storage;
        _logger = logger;
    }

    public int Count { get; private set; }

    public async Task<DateTimeOffset?> RestoreAsync(CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        if (await _storage.LoadAsync(_key) is not { Length: > 0 } data)
        {
            return null;
        }

        var loaded = watch.ElapsedMilliseconds;
        try
        {
            Close();
            System.IO.Directory.CreateDirectory(_directory);
            await File.WriteAllBytesAsync(_path, data, cancellationToken);
            await using var db = Open();
            var version = await ScalarAsync("SELECT version FROM meta");
            Count = await db.Set<T>().CountAsync(cancellationToken);
            _logger.LogDebug("Restored {Count} rows ({Bytes} bytes) of {Key} in {Ms} ms (storage {Loaded} ms)", Count, data.Length, _key, watch.ElapsedMilliseconds, loaded);
            return new DateTimeOffset(version, TimeSpan.Zero);
        }
        catch (Exception exception) when (exception is SqliteException or IOException or InvalidOperationException or InvalidCastException)
        {
            _logger.LogWarning(exception, "Restoring {Key} failed, it is downloaded again", _key);
            Close();
            File.Delete(_path);
            return null;
        }
    }

    public async Task ReplaceAsync(IReadOnlyCollection<T> rows, DateTimeOffset version, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Close();
            System.IO.Directory.CreateDirectory(_directory);
            File.Delete(_path);
            await using (var db = Open())
            {
                await db.Database.EnsureCreatedAsync(cancellationToken);
            }

            await ExecuteAsync("CREATE TABLE meta (version INTEGER NOT NULL)");
            await ExecuteAsync("INSERT INTO meta VALUES (0)");
            await WriteAsync(rows, [], version, replace: true);
            var written = watch.ElapsedMilliseconds;

            // indexes after the rows: one sort per column instead of an index update per row and column
            await using (var db = Open())
            {
                foreach (var column in db.Model.FindEntityType(typeof(T))!.GetProperties().Where(p => !p.IsPrimaryKey() && CopyModel.IsColumn(p.ClrType)))
                {
                    var name = column.GetColumnName();
                    await ExecuteAsync($"CREATE INDEX \"ix_{name}\" ON {CopyDb<T>.Table} (\"{name}\")");
                    await Pause();
                }
            }

            _logger.LogDebug("Wrote {Count} rows of {Key} in {Written} ms, indexed in {Indexed} ms", rows.Count, _key, written, watch.ElapsedMilliseconds - written);
        }
        finally
        {
            _gate.Release();
        }

        await PersistAsync();
    }

    public ClientEntitySnapshotRequest? Snapshot(string? expand)
    {
        using var db = new CopyDb<T>(new DbContextOptionsBuilder<CopyDb<T>>().UseSqlite("Data Source=:memory:").Options);
        return new ClientEntitySnapshotRequest(
            [.. db.Model.FindEntityType(typeof(T))!.GetProperties().Select(p => new ClientEntitySnapshotColumn(p.GetColumnName(), CopyModel.Kind(p.ClrType), CopyModel.IsColumn(p.ClrType)))],
            expand);
    }

    public async Task ReplaceWithFileAsync(byte[] file, DateTimeOffset version, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Close();
            System.IO.Directory.CreateDirectory(_directory);
            await File.WriteAllBytesAsync(_path, file, cancellationToken);
            await using var db = Open();
            Count = await db.Set<T>().CountAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }

        // kept as it came: no need to read the file back
        await _storage.SaveAsync(_key, file);
    }

    public async Task ApplyAsync(IReadOnlyCollection<T> changed, IReadOnlyCollection<Guid> deleted, DateTimeOffset version, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await WriteAsync(changed, deleted, version, replace: false);
        }
        finally
        {
            _gate.Release();
        }

        if (changed.Count + deleted.Count > 0)
        {
            await PersistAsync();
        }
    }

    public async Task<IReadOnlyList<Guid>> IdsAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = Open();
            return await db.Set<T>().Select(row => EF.Property<Guid>(row, "Id")).ToListAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ODataPage<T>> QueryAsync(ODataQuery query, Func<CancellationToken, Task<IReadOnlyList<FacetGroupDto>>> facets, CancellationToken cancellationToken)
    {
        // the server's facet definitions first: loading them must not hold the gate
        var groups = query.Facets ? await facets(cancellationToken) : [];
        var watch = Stopwatch.StartNew();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = Open();
            var all = db.Set<T>().AsNoTracking();
            var rows = Filtered(all, query.Filter);
            long? count = query.Count ? await rows.LongCountAsync(cancellationToken) : null;
            var counted = watch.ElapsedMilliseconds;
            IReadOnlyList<T> items = query.Top is 0 ? [] : await Ordered(rows, query.OrderBy).Skip(query.Skip ?? 0).Take(query.Top ?? int.MaxValue).ToListAsync(cancellationToken);
            var paged = watch.ElapsedMilliseconds;
            IReadOnlyList<FacetGroupDto> counts = query.Facets ? await FacetsAsync(all, rows, groups, query.FacetContext, _logger, cancellationToken) : [];
            _logger.LogDebug("{Filter} | {OrderBy} -> {Count} rows, page {Items}, {Facets} facet groups: count {Counted} ms, page {Paged} ms, facets {Faceted} ms",
                query.Filter, query.OrderBy, count, items.Count, counts.Count, counted, paged - counted, watch.ElapsedMilliseconds - paged);
            return new ODataPage<T>(items, count, counts);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DropAsync()
    {
        Close();
        File.Delete(_path);
        Count = 0;
        await _storage.SaveAsync(_key, []);
    }

    public ValueTask DisposeAsync()
    {
        Close();
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }

    private static IQueryable<T> Filtered(IQueryable<T> rows, string? filter) =>
        string.IsNullOrWhiteSpace(filter) ? rows : rows.Where(Parameters.Of(ODataLinq.Filter<T>(filter)));

    // "$orderby=Name desc,Id"; without one in the order of the ids, as the in-memory copy (the download order)
    private static IQueryable<T> Ordered(IQueryable<T> rows, string? orderBy)
    {
        var clauses = string.IsNullOrWhiteSpace(orderBy) ? ["Id"] : orderBy.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var first = true;
        foreach (var clause in clauses)
        {
            var parts = clause.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var key = ODataLinq.Key<T>(parts[0].Replace('.', '/'));
            var descending = parts.Length > 1 && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase);
            rows = (IQueryable<T>)OrderMethod.MakeGenericMethod(key.ReturnType).Invoke(null, [rows, key, first, descending])!;
            first = false;
        }

        return rows;
    }

    private static IQueryable<T> Order<TKey>(IQueryable<T> rows, Expression<Func<T, TKey>> key, bool first, bool descending) => (first, descending) switch
    {
        (true, false) => rows.OrderBy(key),
        (true, true) => rows.OrderByDescending(key),
        (false, false) => ((IOrderedQueryable<T>)rows).ThenBy(key),
        _ => ((IOrderedQueryable<T>)rows).ThenByDescending(key),
    };

    // the server's groups with local counts; a chosen "any of" group counts on the rows of the other choices, as the server does
    private static async Task<IReadOnlyList<FacetGroupDto>> FacetsAsync(IQueryable<T> all, IQueryable<T> rows, IReadOnlyList<FacetGroupDto> groups, FacetContextDto? context,
        ILogger logger, CancellationToken cancellationToken)
    {
        var counted = new Dictionary<(string? Scope, string Path), List<(object? Value, int Count)>>();
        var result = new List<FacetGroupDto>(groups.Count);
        var sincePause = Stopwatch.StartNew();
        foreach (var group in groups)
        {
            var own = context is not null && context.Groups.ContainsKey(group.Key) && context.Combines?.GetValueOrDefault(group.Key) != FacetCombine.All;
            var filter = own ? context!.Without(group.Key) : null;
            var basis = own ? Filtered(all, filter) : rows;
            var options = new List<FacetOptionDto>();
            var groupWatch = Stopwatch.StartNew();
            var queriesBefore = counted.Count;
            var ranged = group.Type is FacetKind.Range or FacetKind.DateRange ? await RangeCountsAsync(group.Options, basis, cancellationToken) : null;
            for (var i = 0; i < group.Options.Count; i++)
            {
                var option = group.Options[i];
                int count;
                try
                {
                    count = ranged?[i] ?? await OptionCountAsync(option, basis, own ? filter : AllRows, counted, cancellationToken);
                }
                catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException)
                {
                    // ponytail: an option SQL cannot read (Tags/any(...)) keeps the server's count
                    count = option.Count;
                }

                if (count > 0 || group.Type is FacetKind.Range or FacetKind.DateRange)
                {
                    options.Add(option with { Count = count });
                }
            }

            result.Add(group with { Options = options, SampleRows = null, TotalRows = null });
            logger.LogDebug("Facet {Group} ({Type}, {Options} options, {Grouped} group-bys): {Ms} ms",
                group.Key, group.Type, group.Options.Count, counted.Count - queriesBefore, groupWatch.ElapsedMilliseconds);

            // the first count of a query shape compiles it (about a third of a second in the browser): the page renders between groups that took a while
            if (sincePause.ElapsedMilliseconds > 40)
            {
                await Pause();
                sincePause.Restart();
            }
        }

        return result;
    }

    // an option on one top-level property: the column's values counted once (GROUP BY), the option decided per value; others one COUNT
    private static async Task<int> OptionCountAsync(FacetOptionDto option, IQueryable<T> basis, string? scope,
        Dictionary<(string? Scope, string Path), List<(object? Value, int Count)>> counted, CancellationToken cancellationToken)
    {
        if (SinglePath(ODataLinq.Parse(option.OData)) is not { } path || path.Contains('/'))
        {
            return await basis.Where(Parameters.Of(ODataLinq.Filter<T>(option.OData))).CountAsync(cancellationToken);
        }

        if (!counted.TryGetValue((scope, path), out var values))
        {
            var key = ODataLinq.Key<T>(path);
            values = counted[(scope, path)] = await (Task<List<(object?, int)>>)CountsMethod.MakeGenericMethod(key.ReturnType).Invoke(null, [basis, key, cancellationToken])!;
        }

        // "P eq value", most options of a list: looked up instead of tested against every value
        var valueType = ODataLinq.Key<T>(path).ReturnType;
        if (ODataLinq.Parse(option.OData) is BinaryOperatorToken { OperatorKind: BinaryOperatorKind.Equal, Left: PathToken, Right: LiteralToken literal })
        {
            var lookup = Lookups.GetValue(values, v => v.Where(x => x.Value is not null).ToDictionary(x => x.Value!, x => x.Count));
            return ODataLinq.Literal(literal.Value, valueType) is { } wanted ? lookup.GetValueOrDefault(wanted) : values.Where(x => x.Value is null).Sum(x => x.Count);
        }

        var accepts = Predicates.GetOrAdd(option.OData, text => ODataLinq.Filter<T>(text).Compile());
        var property = typeof(T).GetProperty(path, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
            ?? throw new NotSupportedException($"{typeof(T).Name} has no property {path}.");
        var probe = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
        var count = 0;
        foreach (var (value, n) in values)
        {
            property.SetValue(probe, value);
            if (accepts(probe))
            {
                count += n;
            }
        }

        return count;
    }

    private sealed record Range(IComparable? Low, bool LowIncluded, IComparable? High, bool HighIncluded);

    private static readonly ConditionalWeakTable<List<(object? Value, int Count)>, Dictionary<object, int>> Lookups = new();

    private static readonly HashSet<Type> OrderedTypes =
    [
        typeof(byte), typeof(sbyte), typeof(short), typeof(ushort), typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal),
        typeof(DateTime), typeof(DateTimeOffset), typeof(DateOnly), typeof(TimeOnly), typeof(TimeSpan),
    ];

    // the options of a range group on one column ("Total ge 10 and Total lt 20", the months of a date): one statement puts every row into the interval
    // between the options' bounds it falls in (CASE), each option adds up its intervals. A GROUP BY of the column brought every distinct value into the
    // browser's .NET: 100,000 order dates took more than a second, this a few milliseconds. Null when an option is no range on that column.
    private static async Task<int[]?> RangeCountsAsync(IReadOnlyList<FacetOptionDto> options, IQueryable<T> basis, CancellationToken cancellationToken)
    {
        string? path = null;
        LambdaExpression? key = null;
        Type? valueType = null;
        var ranges = new List<Range>(options.Count);
        foreach (var option in options)
        {
            var token = ODataLinq.Parse(option.OData);
            if (SinglePath(token) is not { } own || own.Contains('/') || (path is not null && !string.Equals(own, path, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            if (key is null)
            {
                path = own;
                key = ODataLinq.Key<T>(own);
                valueType = Nullable.GetUnderlyingType(key.ReturnType) ?? key.ReturnType;
                if (!OrderedTypes.Contains(valueType))
                {
                    return null;
                }
            }

            if (Bounds(token, valueType!) is not { } range)
            {
                return null;
            }

            ranges.Add(range);
        }

        if (key is null)
        {
            return null;
        }

        // bucket 2i: below bound i (and above the one before), 2i+1: equal to bound i, 2n: above every bound, -1: null
        var bounds = ranges.SelectMany(r => new[] { r.Low, r.High }).OfType<IComparable>().Distinct().Order(Comparer<IComparable>.Default).ToList();
        var member = key.Body;
        Expression bucket = Expression.Constant(2 * bounds.Count);
        for (var i = bounds.Count - 1; i >= 0; i--)
        {
            var bound = Expression.Constant(bounds[i], member.Type);
            bucket = Expression.Condition(Expression.LessThan(member, bound), Expression.Constant(2 * i),
                Expression.Condition(Expression.Equal(member, bound), Expression.Constant(2 * i + 1), bucket));
        }

        if (!member.Type.IsValueType || Nullable.GetUnderlyingType(member.Type) is not null)
        {
            bucket = Expression.Condition(Expression.Equal(member, Expression.Constant(null, member.Type)), Expression.Constant(-1), bucket);
        }

        var counts = (await basis.GroupBy(Expression.Lambda<Func<T, int>>(bucket, key.Parameters)).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken))
            .ToDictionary(g => g.Key, g => g.Count);
        var index = bounds.Select((b, i) => (b, i)).ToDictionary(x => (object)x.b, x => x.i);
        var result = new int[ranges.Count];
        for (var r = 0; r < ranges.Count; r++)
        {
            var range = ranges[r];
            var from = range.Low is null ? 0 : 2 * index[range.Low] + (range.LowIncluded ? 1 : 2);
            var to = range.High is null ? 2 * bounds.Count : 2 * index[range.High] + (range.HighIncluded ? 1 : 0);
            for (var b = from; b <= to; b++)
            {
                result[r] += counts.GetValueOrDefault(b);
            }
        }

        return result;
    }

    // "P ge a", "P lt b", "P eq c" and their "and": one interval of the column's values; null for anything else
    private static Range? Bounds(QueryToken token, Type type)
    {
        switch (token)
        {
            case BinaryOperatorToken { OperatorKind: BinaryOperatorKind.And } and:
                return Bounds(and.Left, type) is { } left && Bounds(and.Right, type) is { } right ? Intersect(left, right) : null;
            case BinaryOperatorToken { Left: PathToken, Right: LiteralToken literal } compare:
                return Compare(compare.OperatorKind, ODataLinq.Literal(literal.Value, type) as IComparable);
            case BinaryOperatorToken { Left: LiteralToken literal, Right: PathToken } compare:
                return Compare(compare.OperatorKind switch
                {
                    BinaryOperatorKind.GreaterThan => BinaryOperatorKind.LessThan,
                    BinaryOperatorKind.GreaterThanOrEqual => BinaryOperatorKind.LessThanOrEqual,
                    BinaryOperatorKind.LessThan => BinaryOperatorKind.GreaterThan,
                    BinaryOperatorKind.LessThanOrEqual => BinaryOperatorKind.GreaterThanOrEqual,
                    var same => same,
                }, ODataLinq.Literal(literal.Value, type) as IComparable);
            default:
                return null;
        }
    }

    private static Range? Compare(BinaryOperatorKind kind, IComparable? value) => value is null ? null : kind switch
    {
        BinaryOperatorKind.GreaterThan => new Range(value, false, null, false),
        BinaryOperatorKind.GreaterThanOrEqual => new Range(value, true, null, false),
        BinaryOperatorKind.LessThan => new Range(null, false, value, false),
        BinaryOperatorKind.LessThanOrEqual => new Range(null, false, value, true),
        BinaryOperatorKind.Equal => new Range(value, true, value, true),
        _ => null,
    };

    private static Range Intersect(Range a, Range b)
    {
        var (low, lowIncluded) = a.Low is null ? (b.Low, b.LowIncluded) : b.Low is null ? (a.Low, a.LowIncluded)
            : a.Low.CompareTo(b.Low) switch { > 0 => (a.Low, a.LowIncluded), < 0 => (b.Low, b.LowIncluded), _ => (a.Low, a.LowIncluded && b.LowIncluded) };
        var (high, highIncluded) = a.High is null ? (b.High, b.HighIncluded) : b.High is null ? (a.High, a.HighIncluded)
            : a.High.CompareTo(b.High) switch { < 0 => (a.High, a.HighIncluded), > 0 => (b.High, b.HighIncluded), _ => (a.High, a.HighIncluded && b.HighIncluded) };
        return new Range(low, lowIncluded, high, highIncluded);
    }

    private static async Task<List<(object?, int)>> CountsAsync<TKey>(IQueryable<T> rows, Expression<Func<T, TKey>> key, CancellationToken cancellationToken) =>
        [.. (await rows.GroupBy(key).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken)).Select(g => ((object?)g.Key, g.Count))];

    // the one path a filter reads, or null when it reads several or none
    private static string? SinglePath(QueryToken token)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Collect(token, paths);
        return paths.Count == 1 ? paths.First() : null;
    }

    private static void Collect(QueryToken? token, HashSet<string> paths)
    {
        switch (token)
        {
            case PathToken path:
                paths.Add(ODataLinq.PathText(path));
                break;
            case BinaryOperatorToken binary:
                Collect(binary.Left, paths);
                Collect(binary.Right, paths);
                break;
            case UnaryOperatorToken unary:
                Collect(unary.Operand, paths);
                break;
            case InToken @in:
                Collect(@in.Left, paths);
                Collect(@in.Right, paths);
                break;
            case FunctionCallToken function:
                foreach (var argument in function.Arguments)
                {
                    Collect(argument.ValueToken, paths);
                }

                break;
            case LiteralToken:
                break;
            default:
                // lambdas (any/all) and anything else: not one column
                paths.Add("\u0002" + token?.Kind);
                break;
        }
    }

    // ADO with EF's column mapping: EF's change tracking would take far longer for thousands of rows in the browser
    private async Task WriteAsync(IReadOnlyCollection<T> changed, IReadOnlyCollection<Guid> deleted, DateTimeOffset version, bool replace)
    {
        await using var db = Open();
        var entity = db.Model.FindEntityType(typeof(T))!;
        var mapped = entity.GetProperties().Select(p => (Column: p.GetColumnName(), Get: CopyModel.Getter(p.PropertyInfo!), Convert: p.GetTypeMapping().Converter)).ToList();
        var idOf = mapped.First(m => m.Column == "Id").Get;
        var connection = Connection();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = $"DELETE FROM {CopyDb<T>.Table} WHERE Id = $id";
            var id = delete.Parameters.Add("$id", SqliteType.Text);
            foreach (var key in replace ? deleted : deleted.Concat(changed.Select(row => (Guid)idOf(row)!)))
            {
                id.Value = key.ToString().ToUpperInvariant();
                await delete.ExecuteNonQueryAsync();
            }
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = $"INSERT INTO {CopyDb<T>.Table} ({string.Join(",", mapped.Select(m => "\"" + m.Column + "\""))}) VALUES ({string.Join(",", mapped.Select((_, i) => "$p" + i))})";
            var parameters = mapped.Select((_, i) => insert.Parameters.Add(new SqliteParameter("$p" + i, null))).ToList();
            var written = 0;
            foreach (var row in changed)
            {
                for (var i = 0; i < mapped.Count; i++)
                {
                    var value = mapped[i].Get(row);
                    value = value is null ? null : mapped[i].Convert is { } converter ? converter.ConvertToProvider(value) : value;
                    parameters[i].Value = value switch
                    {
                        null => DBNull.Value,
                        Guid guid => guid.ToString().ToUpperInvariant(),
                        Enum e => Convert.ToInt64(e, System.Globalization.CultureInfo.InvariantCulture),
                        _ => value,
                    };
                }

                await insert.ExecuteNonQueryAsync();

                // Microsoft.Data.Sqlite runs synchronously: the page renders between slices
                if (++written % 500 == 0)
                {
                    await Pause();
                }
            }
        }

        await using (var meta = connection.CreateCommand())
        {
            meta.Transaction = transaction;
            meta.CommandText = "UPDATE meta SET version = $v";
            meta.Parameters.AddWithValue("$v", version.UtcTicks);
            await meta.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
        Count = await db.Set<T>().CountAsync();
    }

    // the file's bytes go to the browser storage: a reload opens them instead of downloading or parsing the rows
    private async Task PersistAsync()
    {
        var watch = Stopwatch.StartNew();
        try
        {
            // shared: the connection keeps the file open (a sharing violation on Windows, where the tests run)
            byte[] data;
            await using (var file = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                data = new byte[file.Length];
                await file.ReadExactlyAsync(data);
            }

            await _storage.SaveAsync(_key, data);
            _logger.LogDebug("Kept {Bytes} bytes of {Key} in {Ms} ms", data.Length, _key, watch.ElapsedMilliseconds);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Keeping {Key} failed", _key);
        }
    }

    private static Task Pause() => Task.Delay(1);

    private SqliteConnection Connection()
    {
        if (_connection is null)
        {
            _connection = new SqliteConnection($"Data Source={_path};Pooling=False");
            _connection.Open();
        }

        return _connection;
    }

    private CopyDb<T> Open() => new(_options ??= new DbContextOptionsBuilder<CopyDb<T>>().UseSqlite(Connection()).Options);

    private void Close()
    {
        _connection?.Dispose();
        _connection = null;
        _options = null;
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var command = Connection().CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> ScalarAsync(string sql)
    {
        await using var command = Connection().CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
