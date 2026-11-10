using System.Reflection;
using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;
using Coworkee.OData.ClientEntities;

namespace Coworkee.Client.Blazor.ClientEntities.Sqlite.Tests;

// Every value the copy keeps must read back the same, whether the browser wrote the rows (ReplaceAsync) or the server sent them as a
// snapshot file (ClientEntitySnapshot.Write), and must filter like the rows themselves.
public sealed class CopyRoundTripTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "coworkee-copy-tests-" + Guid.NewGuid().ToString("N"));
    private readonly MemoryStorage _storage = new();
    private static readonly DateTimeOffset Version = new(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);

    public enum Level { Low, Middle, High }

    [Flags]
    public enum Perks { None = 0, Express = 1, Gift = 2, Insured = 4 }

    public sealed record Inner(string Code, int Size);

    public sealed class Row
    {
        public Guid Id { get; set; }
        public string? Name { get; set; }
        public int Number { get; set; }
        public long Big { get; set; }
        public short Small { get; set; }
        public byte Tiny { get; set; }
        public bool Flag { get; set; }
        public decimal Price { get; set; }
        public double Ratio { get; set; }
        public DateTime At { get; set; }
        public DateTimeOffset When { get; set; }
        public DateTimeOffset? MaybeWhen { get; set; }
        public DateOnly Day { get; set; }
        public DateOnly? MaybeDay { get; set; }
        public TimeOnly Time { get; set; }
        public TimeSpan Span { get; set; }
        public char Letter { get; set; }
        public Level Level { get; set; }
        public Level? MaybeLevel { get; set; }
        public Perks Perks { get; set; }
        public Perks? MaybePerks { get; set; }
        public Guid? Other { get; set; }
        public List<string> Tags { get; set; } = [];
        public Inner? Inner { get; set; }
    }

    private static readonly Row[] Rows = [.. Enumerable.Range(0, 40).Select(i => new Row
    {
        Id = new Guid(i + 1, 0, 0, new byte[8]),
        Name = i % 7 == 0 ? null : $"Row {i:00}",
        Number = i,
        Big = 10_000_000_000L + i,
        Small = (short)(i - 20),
        Tiny = (byte)(i * 3),
        Flag = i % 2 == 0,
        Price = 10.25m + i,
        Ratio = i / 8d,
        At = new DateTime(2026, 1, 1, 8, 30, 15, DateTimeKind.Utc).AddHours(i * 13).AddTicks(i),
        When = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.FromHours(i % 3)).AddDays(i).AddMilliseconds(i),
        MaybeWhen = i % 4 == 0 ? null : new DateTimeOffset(2025, 12, 31, 23, 0, 0, TimeSpan.Zero).AddMinutes(i),
        Day = new DateOnly(2026, 3, 1).AddDays(i - 20),
        MaybeDay = i % 3 == 0 ? null : new DateOnly(2024, 2, 29).AddDays(i),
        Time = new TimeOnly(6, 0).Add(TimeSpan.FromMinutes(i * 37.5)),
        Span = TimeSpan.FromHours(i) - TimeSpan.FromMinutes(90),
        Letter = (char)('A' + i % 26),
        Level = (Level)(i % 3),
        MaybeLevel = i % 5 == 0 ? null : (Level)((i + 1) % 3),
        Perks = (Perks)(i % 8),
        MaybePerks = i % 5 == 0 ? null : (Perks)((i + 3) % 8),
        Other = i % 2 == 0 ? null : Guid.NewGuid(),
        Tags = [.. Enumerable.Range(0, i % 3).Select(t => "tag" + t)],
        Inner = i % 6 == 0 ? null : new Inner("C" + i, i * 2),
    })];

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task Rows_written_in_the_browser_read_back_unchanged()
    {
        await using var store = Store("browser");
        await store.ReplaceAsync(Rows, Version, TestContext.Current.CancellationToken);

        ShouldMatch(await AllAsync(store), Rows);
    }

    [Fact]
    public async Task A_snapshot_of_the_server_reads_back_unchanged()
    {
        await using var store = await SnapshotAsync();

        store.Count.ShouldBe(Rows.Length);
        ShouldMatch(await AllAsync(store), Rows);
    }

    [Fact]
    public async Task A_kept_copy_restores_with_its_version()
    {
        await using (var store = Store("kept"))
        {
            await store.ReplaceAsync(Rows, Version, TestContext.Current.CancellationToken);
        }

        await using var restored = Store("kept");
        (await restored.RestoreAsync(TestContext.Current.CancellationToken)).ShouldBe(Version);
        restored.Count.ShouldBe(Rows.Length);
    }

    [Fact]
    public async Task A_disposed_store_opens_no_connection_again_and_its_copy_stays_for_the_next_visit()
    {
        var store = Store("disposed");
        await store.ReplaceAsync(Rows, Version, TestContext.Current.CancellationToken);

        await store.DisposeAsync();

        // an InvalidOperationException, which the set takes as a query the copy cannot answer: it goes to the server
        await Should.ThrowAsync<ObjectDisposedException>(() => store.QueryAsync(new ODataQuery(), NoFacets, TestContext.Current.CancellationToken));
        await using var next = Store("disposed");
        (await next.RestoreAsync(TestContext.Current.CancellationToken)).ShouldBe(Version);
    }

    [Fact]
    public async Task Rows_applied_again_unchanged_are_no_change_and_the_file_is_not_kept_again()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var store = Store("again");
        await store.ReplaceAsync(Rows, Version, ct);
        var saves = _storage.Saves;

        (await store.ApplyAsync(Rows, [], Version.AddMinutes(1), ct)).ShouldBeFalse();
        (await store.ApplyAsync([], [Guid.NewGuid()], Version.AddMinutes(2), ct)).ShouldBeFalse("a delete of a row the copy no longer has");

        _storage.Saves.ShouldBe(saves);
        ShouldMatch(await AllAsync(store), Rows);
        var added = Clone(Rows[2]);
        added.Id = Guid.NewGuid();
        (await store.ApplyAsync([added], [], Version.AddMinutes(3), ct)).ShouldBeTrue();
        (await store.ApplyAsync([], [Rows[5].Id], Version.AddMinutes(4), ct)).ShouldBeTrue();
        _storage.Saves.ShouldBe(saves + 2);
        ShouldMatch(await AllAsync(store), [.. Rows.Where(r => r.Id != Rows[5].Id), added]);
    }

    // the server writes JSON columns with its own names (and its types' members), so its rows count as changed once, then they are in the browser's format
    [Fact]
    public async Task Rows_of_a_server_snapshot_count_as_changed_at_most_once()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var store = await SnapshotAsync();

        await store.ApplyAsync(Rows, [], Version.AddMinutes(1), ct);

        (await store.ApplyAsync(Rows, [], Version.AddMinutes(2), ct)).ShouldBeFalse();
        ShouldMatch(await AllAsync(store), Rows);
    }

    public static TheoryData<string> Changes() =>
        ["Tags", "Inner", "Name to null", "null to Name", "MaybeWhen to null", "Offset", "MaybeDay", "Price", "Level", "Time", "At", "Letter", "Flag", "Other"];

    [Theory]
    [MemberData(nameof(Changes))]
    public async Task A_change_of_any_kept_value_is_written_and_kept(string change)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var store = Store("change");
        await store.ReplaceAsync(Rows, Version, ct);
        var saves = _storage.Saves;
        var row = Clone(Rows[change == "null to Name" ? 0 : 1]);
        switch (change)
        {
            case "Tags": row.Tags = [.. row.Tags, "new"]; break;
            case "Inner": row.Inner = row.Inner! with { Size = row.Inner.Size + 1 }; break;
            case "Name to null": row.Name = null; break;
            case "null to Name": row.Name = "named"; break;
            case "MaybeWhen to null": row.MaybeWhen = null; break;
            case "Offset": row.When = row.When.ToOffset(row.When.Offset + TimeSpan.FromHours(1)); break;
            case "MaybeDay": row.MaybeDay = row.MaybeDay!.Value.AddDays(1); break;
            case "Price": row.Price += 0.01m; break;
            case "Level": row.Level = Level.High; break;
            case "Time": row.Time = row.Time.Add(TimeSpan.FromTicks(1)); break;
            case "At": row.At = row.At.AddTicks(1); break;
            case "Letter": row.Letter = 'z'; break;
            case "Flag": row.Flag = !row.Flag; break;
            default: row.Other = null; break;
        }

        (await store.ApplyAsync([row], [], Version.AddMinutes(1), ct)).ShouldBeTrue(change);

        _storage.Saves.ShouldBe(saves + 1);
        ShouldMatch(await AllAsync(store), [.. Rows.Select(r => r.Id == row.Id ? row : r)]);
    }

    private static Row Clone(Row row) => System.Text.Json.JsonSerializer.Deserialize<Row>(System.Text.Json.JsonSerializer.Serialize(row))!;

    public static TheoryData<string> Filters() =>
    [
        "Day ge 2026-03-01",
        "Day lt 2026-02-20 or Day eq 2026-03-10",
        "MaybeDay eq null",
        "Letter eq 'C'",
        "Level eq 'High'",
        "MaybeLevel ne 'Low'",
        // as the query builder writes enums and flags
        "(Level eq 'Low' or Level eq 'High')",
        "not ((Level eq 'Low' or Level eq 'High'))",
        "Level in ('Low','High')",
        "Perks has 'Gift'",
        "(Perks has 'Express' or Perks has 'Insured')",
        "not (Perks has 'Gift')",
        "MaybePerks has 'Express'",
        "Perks eq 'Express'",
        "Price gt 30.25",
        "When ge 2026-02-20T00:00:00Z",
        "MaybeWhen lt 2026-01-01T00:00:00Z",
        "At lt 2026-01-10T00:00:00Z",
        "Flag eq true and Small lt 0",
        "Big ge 10000000030",
        "contains(Name,'1')",
        "Name eq null",
        "Span gt duration'PT10H'",
        "Time lt 12:00:00",
    ];

    [Theory]
    [MemberData(nameof(Filters))]
    public async Task Filters_answer_as_on_the_rows(string filter)
    {
        await using var browser = Store("browser-filter");
        await browser.ReplaceAsync(Rows, Version, TestContext.Current.CancellationToken);
        await using var server = await SnapshotAsync();
        var expected = Rows.AsQueryable().Where(ODataLinq.Filter<Row>(filter)).Select(r => r.Id).Order().ToList();

        foreach (var store in new[] { browser, server })
        {
            ODataPage<Row> page;
            try
            {
                page = await store.QueryAsync(new ODataQuery { Filter = filter }, NoFacets, TestContext.Current.CancellationToken);
            }
            catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException or ArgumentException or FormatException or InvalidCastException)
            {
                // the server answers it: never a wrong answer
                Console.WriteLine($"{filter}: answered by the server ({exception.GetType().Name}: {exception.Message})");
                continue;
            }

            page.Count.ShouldBe(expected.Count, filter);
            page.Items.Select(r => r.Id).Order().ToList().ShouldBe(expected, filter);
        }
    }

    [Fact]
    public async Task Orders_by_dates_and_times_as_their_values()
    {
        await using var store = await SnapshotAsync();
        foreach (var (orderBy, key) in new (string, Func<Row, object?>)[] { ("Day desc", r => r.Day), ("Time", r => r.Time), ("Span", r => r.Span), ("When", r => r.When) })
        {
            var page = await store.QueryAsync(new ODataQuery { OrderBy = orderBy + ",Id" }, NoFacets, TestContext.Current.CancellationToken);
            var expected = (orderBy.EndsWith(" desc", StringComparison.Ordinal) ? Rows.OrderByDescending(key) : Rows.OrderBy(key)).ThenBy(r => r.Id).Select(r => r.Id).ToList();
            page.Items.Select(r => r.Id).ToList().ShouldBe(expected, orderBy);
        }
    }

    public static TheoryData<string, string[]> RangeGroups() => new()
    {
        { "Day", ["Day lt 2026-02-15", "Day ge 2026-02-15 and Day lt 2026-03-01", "Day ge 2026-03-01 and Day lt 2026-03-15", "Day ge 2026-03-15"] },
        { "MaybeDay", ["MaybeDay le 2024-03-10", "MaybeDay gt 2024-03-10 and MaybeDay le 2024-04-01", "MaybeDay eq 2024-03-05"] },
        { "When", ["When lt 2026-02-10T00:00:00Z", "When ge 2026-02-10T00:00:00Z and When lt 2026-02-25T12:00:00+02:00", "When ge 2026-02-25T12:00:00+02:00"] },
        { "MaybeWhen", ["MaybeWhen lt 2026-01-01T00:00:00Z", "MaybeWhen ge 2026-01-01T00:00:00Z"] },
        { "Price", ["Price lt 20", "Price ge 20 and Price lt 30.25", "Price ge 30.25 and Price le 40.25", "Price gt 40.25", "Price ge 15 and Price lt 45"] },
        { "Number", ["Number lt 10", "Number ge 10 and Number lt 10", "Number eq 39", "Number gt 39"] },
        { "Span", ["Span lt duration'PT0S'", "Span ge duration'PT0S' and Span lt duration'PT12H'", "Span ge duration'PT12H'"] },
    };

    [Theory]
    [MemberData(nameof(RangeGroups))]
    public async Task Range_facets_count_as_on_the_rows(string path, string[] filters)
    {
        await using var store = await SnapshotAsync();
        var group = new FacetGroupDto(path, path, path, FacetKind.Range, default, [.. filters.Select(f => new FacetOptionDto(default, f, true, -1, f))], true, 0);

        var page = await store.QueryAsync(new ODataQuery { Top = 0, Facets = true, Filter = "Number ne 3" },
            _ => Task.FromResult<IReadOnlyList<FacetGroupDto>>([group]), TestContext.Current.CancellationToken);

        var rows = Rows.Where(r => r.Number != 3).AsQueryable();
        page.Facets.Single().Options.Select(o => (o.OData, o.Count)).ShouldBe(filters.Select(f => (f, rows.Count(ODataLinq.Filter<Row>(f)))));
    }

    [Fact]
    public async Task List_facets_count_as_on_the_rows_also_for_a_chosen_group()
    {
        await using var store = await SnapshotAsync();
        string[] level = ["Level eq 'Low'", "Level eq 'Middle'", "Level eq 'High'"];
        string[] letter = ["Letter eq 'A'", "Letter eq 'B'", "Letter eq 'Z'"];
        string[] name = ["Name eq null", "Name eq 'Row 01'", "contains(Name,'2')"];
        FacetGroupDto Group(string key, string[] filters) =>
            new(key, key, key, FacetKind.CheckboxList, default, [.. filters.Select(f => new FacetOptionDto(default, f, true, -1, f))], true, 0);

        // the same counts for two filter values in a row: the second runs the compiled statements with other parameters
        foreach (var below in new[] { 30, 12 })
        {
            var filter = $"Number lt {below}";
            var page = await store.QueryAsync(new ODataQuery { Top = 0, Facets = true, Filter = filter },
                _ => Task.FromResult<IReadOnlyList<FacetGroupDto>>([Group("Level", level), Group("Letter", letter), Group("Name", name)]), TestContext.Current.CancellationToken);

            var rows = Rows.Where(r => r.Number < below).AsQueryable();
            var expected = level.Concat(letter).Concat(name).Select(f => (f, rows.Count(ODataLinq.Filter<Row>(f)))).Where(x => x.Item2 > 0);
            page.Facets.SelectMany(g => g.Options).Select(o => (o.OData, o.Count)).ShouldBe(expected, filter);
        }
    }

    private readonly ListLogger _log = new();

    private SqliteClientEntityStore<Row> Store(string key) => new(key, _storage, _log, _directory);

    private async Task<SqliteClientEntityStore<Row>> SnapshotAsync()
    {
        var store = Store("server");
        var request = store.Snapshot(null).ShouldNotBeNull();
        var file = Path.Combine(_directory, "snapshot.db");
        Directory.CreateDirectory(_directory);

        // the rows as the server's OData read hands them over: property name to CLR value
        var properties = typeof(Row).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        IReadOnlyList<IReadOnlyDictionary<string, object?>> shaped = [.. Rows.Select(r => (IReadOnlyDictionary<string, object?>)properties.ToDictionary(p => p.Name, p => p.GetValue(r)))];
        ClientEntitySnapshot.Write(file, request.Columns, shaped, Version);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        await store.ReplaceWithFileAsync(await File.ReadAllBytesAsync(file), Version, TestContext.Current.CancellationToken);
        return store;
    }

    private static async Task<IReadOnlyList<Row>> AllAsync(SqliteClientEntityStore<Row> store) =>
        (await store.QueryAsync(new ODataQuery { OrderBy = "Id" }, NoFacets, TestContext.Current.CancellationToken)).Items;

    private static Task<IReadOnlyList<FacetGroupDto>> NoFacets(CancellationToken _) => Task.FromResult<IReadOnlyList<FacetGroupDto>>([]);

    private static void ShouldMatch(IReadOnlyList<Row> actual, IReadOnlyList<Row> expected)
    {
        actual.Count.ShouldBe(expected.Count);
        var byId = actual.ToDictionary(r => r.Id);
        foreach (var e in expected)
        {
            var a = byId[e.Id];
            a.Name.ShouldBe(e.Name);
            a.Number.ShouldBe(e.Number);
            a.Big.ShouldBe(e.Big);
            a.Small.ShouldBe(e.Small);
            a.Tiny.ShouldBe(e.Tiny);
            a.Flag.ShouldBe(e.Flag);
            a.Price.ShouldBe(e.Price);
            a.Ratio.ShouldBe(e.Ratio);
            a.At.Ticks.ShouldBe(e.At.Ticks);
            a.When.ShouldBe(e.When);
            a.When.Offset.ShouldBe(e.When.Offset);
            a.MaybeWhen.ShouldBe(e.MaybeWhen);
            a.Day.ShouldBe(e.Day);
            a.MaybeDay.ShouldBe(e.MaybeDay);
            a.Time.ShouldBe(e.Time);
            a.Span.ShouldBe(e.Span);
            a.Letter.ShouldBe(e.Letter);
            a.Level.ShouldBe(e.Level);
            a.MaybeLevel.ShouldBe(e.MaybeLevel);
            a.Perks.ShouldBe(e.Perks);
            a.MaybePerks.ShouldBe(e.MaybePerks);
            a.Other.ShouldBe(e.Other);
            a.Tags.ShouldBe(e.Tags);
            a.Inner.ShouldBe(e.Inner);
        }
    }

    private sealed class ListLogger : Microsoft.Extensions.Logging.ILogger
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }

    private sealed class MemoryStorage : IClientEntityStorage
    {
        private readonly Dictionary<string, byte[]> _data = [];

        public Task<byte[]?> LoadAsync(string key) => Task.FromResult(_data.GetValueOrDefault(key));

        public int Saves { get; private set; }

        public Task SaveAsync(string key, byte[] data)
        {
            Saves++;
            _data[key] = data;
            return Task.CompletedTask;
        }

        public Task ClearAsync()
        {
            _data.Clear();
            return Task.CompletedTask;
        }
    }
}
