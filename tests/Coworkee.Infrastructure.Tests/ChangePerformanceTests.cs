using System.Diagnostics;
using Coworkee.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Coworkee.Infrastructure.Tests;

/// <summary>
/// Saving 1,000 audited entities (insert, then update) must not get slower than before the change scans were merged into one collector. The baseline is
/// the best of three runs measured on the old code (see the spec, section "Phase 2 as built"); the bound leaves room for slower machines.
/// </summary>
public sealed class ChangePerformanceTests(DatabaseFixture database) : IAsyncLifetime
{
    private const int Count = 1_000;

    /// <summary>Best of three, insert + update of 1,000 documents, measured before the collector (Postgres container, dev machine).</summary>
    private static readonly TimeSpan Baseline = TimeSpan.FromMilliseconds(BaselineMilliseconds);

    private const int BaselineMilliseconds = 900;

    private readonly TestCurrentUser _user = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero));

    public async ValueTask InitializeAsync() => await database.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Saving_a_thousand_entities_stays_as_fast_as_before()
    {
        var best = TimeSpan.MaxValue;
        for (var run = 0; run < 3; run++)
        {
            var elapsed = await RunAsync();
            best = elapsed < best ? elapsed : best;
        }

        var line = $"insert + update of {Count} documents: best {best.TotalMilliseconds:0} ms (baseline {BaselineMilliseconds} ms)";
        TestContext.Current.TestOutputHelper?.WriteLine(line);
        await File.AppendAllTextAsync(Path.Combine(Path.GetTempPath(), "coworkee-change-performance.txt"), $"{DateTimeOffset.Now:O} {line}{Environment.NewLine}", TestContext.Current.CancellationToken);
        best.ShouldBeLessThan(Baseline * 2.5);
    }

    private async Task<TimeSpan> RunAsync()
    {
        await using var provider = database.CreateServices(_user, _clock);
        var watch = Stopwatch.StartNew();
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            db.AddRange(Enumerable.Range(0, Count).Select(i => new Document { Title = $"Document {i}", Secret = "s", Note = "n" }));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            foreach (var document in db.Documents.Local)
            {
                document.Title += " changed";
                document.Settings.Color = "red";
            }

            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        return watch.Elapsed;
    }
}
