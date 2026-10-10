using System.Diagnostics;
using System.Text.Json;
using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;

namespace Coworkee.Client.Blazor.ClientEntities.Tests;

public sealed class LargeSetTests(ITestOutputHelper output)
{
    private const int Rows = 100_000;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Queries_over_100k_rows_stay_fast()
    {
        var server = new FakeServer { MaxRows = Rows, BatchSize = 5000 };
        var brands = Enumerable.Range(0, 300).Select(i => new Brand(Guid.CreateVersion7(), $"Brand {i:000}")).ToArray();
        var categories = new[] { "Tools", "Light", "Garden", "Kitchen", "Office", "Outdoor" };
        server.Rows.AddRange(Enumerable.Range(0, Rows).Select(i => new Gadget(Guid.CreateVersion7(), $"Gadget {i * 7919 % Rows:000000}", categories[i % categories.Length],
            i % 997, brands[i % brands.Length], (Kind)(i % 2), DateTimeOffset.Parse("2026-01-01T00:00:00Z").AddMinutes(i))));
        server.Facets =
        [
            new FacetGroupDto("category", "Category", "Category", FacetKind.CheckboxList, FacetOperator.Or,
                [.. categories.Select(c => new FacetOptionDto(JsonSerializer.SerializeToElement(c), c, true, 0, $"Category eq '{c}'"))], true, 0),
            new FacetGroupDto("price", "Price", "Price", FacetKind.Range, FacetOperator.Or,
                [new FacetOptionDto(JsonSerializer.SerializeToElement("cheap"), "< 50", true, 0, "(Price ge 0 and Price lt 50)"),
                 new FacetOptionDto(JsonSerializer.SerializeToElement("mid"), "50-500", true, 0, "(Price ge 50 and Price lt 500)")], false, 1),
        ];
        await using var set = server.Set();
        var watch = Stopwatch.StartNew();
        await set.RefreshAsync(Ct);
        output.WriteLine($"download {Rows} rows: {watch.ElapsedMilliseconds} ms ({server.Queries.Count} pages)");
        set.Items.Count.ShouldBe(Rows);

        var queries = new (string Name, ODataQuery Query)[]
        {
            ("first page, facets", new ODataQuery { Top = 25, Skip = 0, Facets = true }),
            ("sorted by name", new ODataQuery { OrderBy = "Name asc", Top = 25, Skip = 0, Facets = true }),
            ("sorted by name, page 2", new ODataQuery { OrderBy = "Name asc", Top = 25, Skip = 25, Facets = true }),
            ("grouped by brand, sorted by price", new ODataQuery { OrderBy = "Brand/Name asc,Price desc", Top = 25, Skip = 0, Facets = true }),
            ("search", new ODataQuery { Filter = "(contains(tolower(Name),'12') or contains(tolower(Category),'12'))", OrderBy = "Name asc", Top = 25, Facets = true }),
            ("facet filter", new ODataQuery { Filter = "(Category eq 'Tools' or Category eq 'Garden') and (Price ge 50 and Price lt 500)", OrderBy = "Created desc", Top = 25, Facets = true }),
        };
        foreach (var (name, query) in queries)
        {
            watch.Restart();
            var first = await set.QueryAsync(query, Ct);
            var cold = watch.Elapsed.TotalMilliseconds;
            watch.Restart();
            var next = await set.QueryAsync(query with { Skip = (query.Skip ?? 0) + 25 }, Ct);
            output.WriteLine($"{name}: {cold:0.0} ms, next page {watch.Elapsed.TotalMilliseconds:0.00} ms ({first.Count} rows match)");
            next.Count.ShouldBe(first.Count);
            // generous bounds: they catch a quadratic slip, not a slow machine
            cold.ShouldBeLessThan(2000);
            watch.Elapsed.TotalMilliseconds.ShouldBeLessThan(100);
        }
    }
}
