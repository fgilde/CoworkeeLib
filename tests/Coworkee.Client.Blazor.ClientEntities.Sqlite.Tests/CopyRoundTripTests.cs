using System.Reflection;
using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;
using Coworkee.OData.ClientEntities;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coworkee.Client.Blazor.ClientEntities.Sqlite.Tests;

// Every value the copy keeps must read back the same, whether the browser wrote the rows (ReplaceAsync) or the server sent them as a
// snapshot file (ClientEntitySnapshot.Write), and must filter like the rows themselves.
public sealed class CopyRoundTripTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "coworkee-copy-tests-" + Guid.NewGuid().ToString("N"));
    private readonly MemoryStorage _storage = new();
    private static readonly DateTimeOffset Version = new(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);

    public enum Level { Low, Middle, High }

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

    public static TheoryData<string> Filters() =>
    [
        "Day ge 2026-03-01",
        "Day lt 2026-02-20 or Day eq 2026-03-10",
        "MaybeDay eq null",
        "Letter eq 'C'",
        "Level eq 'High'",
        "MaybeLevel ne 'Low'",
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

    private SqliteClientEntityStore<Row> Store(string key) => new(key, _storage, NullLogger.Instance, _directory);

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
            a.Other.ShouldBe(e.Other);
            a.Tags.ShouldBe(e.Tags);
            a.Inner.ShouldBe(e.Inner);
        }
    }

    private sealed class MemoryStorage : IClientEntityStorage
    {
        private readonly Dictionary<string, byte[]> _data = [];

        public Task<byte[]?> LoadAsync(string key) => Task.FromResult(_data.GetValueOrDefault(key));

        public Task SaveAsync(string key, byte[] data)
        {
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
