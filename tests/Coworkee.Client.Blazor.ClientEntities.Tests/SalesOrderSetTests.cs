using System.Diagnostics;
using System.Text.Json;
using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;

namespace Coworkee.Client.Blazor.ClientEntities.Tests;

public enum OrderStatus { Open, Confirmed, Shipped, Delivered, Cancelled }

public enum OrderChannel { Online, Store, Phone, Partner }

[Flags]
public enum OrderOptions { None = 0, Express = 1, GiftWrap = 2, Insured = 4, Priority = 8 }

public sealed record Customer(Guid Id, string Name, string City);

public sealed record Order(Guid Id, string Number, Guid CustomerId, Customer? Customer, OrderStatus Status, OrderChannel Channel, string Region,
    DateTimeOffset OrderDate, int Items, decimal Total, OrderOptions Options = OrderOptions.None, bool Paid = false);

/// <summary>The demo's 100,000 sales orders with the facets its server sends: the case the client mode has to win.</summary>
public sealed class SalesOrderSetTests(ITestOutputHelper output)
{
    private const int Rows = 100_000;
    private static readonly string[] Regions = ["Nord", "Süd", "Ost", "West", "Mitte"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    internal static (List<Order> Orders, IReadOnlyList<FacetGroupDto> Facets) Data()
    {
        var customers = Enumerable.Range(0, 250).Select(c => new Customer(Guid.CreateVersion7(), $"Customer {c:000}", "City")).ToArray();
        var start = DateTimeOffset.Parse("2024-01-01T00:00:00Z");
        var orders = Enumerable.Range(1, Rows).Select(g =>
        {
            var customer = customers[g * 7919 % customers.Length];
            return new Order(Guid.CreateVersion7(), $"SO-{g:0000000}", customer.Id, customer, (OrderStatus)(g % 5), (OrderChannel)(g % 4), Regions[g * 7919 % 250 % 5],
                start.AddMinutes(g * 15), 1 + g * 13 % 12, Math.Round((1 + g * 13 % 12) * (4.9m + g * 104729L % 25000 / 100m), 2), (OrderOptions)(g % 16), g % 2 == 0);
        }).ToList();
        FacetOptionDto Option(string odata, string label = "x") => new(JsonSerializer.SerializeToElement(label), label, true, 0, odata);
        FacetGroupDto Group(string key, string field, FacetKind kind, IEnumerable<FacetOptionDto> options) =>
            new(key, key, field, kind, FacetOperator.Or, [.. options], true, 0);
        var facets = new[]
        {
            Group("status", "Status", FacetKind.CheckboxList, Enum.GetNames<OrderStatus>().Select(s => Option($"Status eq '{s}'", s))),
            Group("channel", "Channel", FacetKind.CheckboxList, Enum.GetNames<OrderChannel>().Select(s => Option($"Channel eq '{s}'", s))),
            Group("options", "Options", FacetKind.CheckboxList, new[] { "Express", "GiftWrap", "Insured", "Priority" }.Select(s => Option($"Options has '{s}'", s))),
            Group("region", "Region", FacetKind.CheckboxList, Regions.Select(r => Option($"Region eq '{r}'", r))),
            Group("customerId", "CustomerId", FacetKind.CheckboxList, customers.Take(50).Select(c => Option($"CustomerId eq {c.Id}", c.Name))),
            Group("orderDate", "OrderDate", FacetKind.DateRange, Enumerable.Range(0, 35).Select(m => Option(
                $"(OrderDate ge {start.AddMonths(m):yyyy-MM-ddTHH:mm:ssZ} and OrderDate lt {start.AddMonths(m + 1):yyyy-MM-ddTHH:mm:ssZ})"))),
            Group("total", "Total", FacetKind.Range, Enumerable.Range(0, 16).Select(b => Option(FormattableString.Invariant($"(Total ge {4.9m + b * 190.87m} and Total lt {4.9m + (b + 1) * 190.87m})")))),
            Group("items", "Items", FacetKind.Range, Enumerable.Range(1, 12).Select(i => Option($"(Items ge {i} and Items lt {i + 1})"))),
            Group("paid", "Paid", FacetKind.CheckboxList, [Option("Paid eq true", "Yes"), Option("Paid eq false", "No")]),
        };
        return (orders, facets);
    }

    [Fact]
    public async Task A_new_filter_with_facets_over_100k_orders_stays_fast()
    {
        var (orders, facets) = Data();
        var table = LocalTable<Order>.Of(orders);
        foreach (var path in table.Indexable)
        {
            table.Column(path).Index();
        }

        facets.SelectMany(g => g.Options).Where(o => LocalFilter.OnColumn(table, ODataLinq.Parse(o.OData)) is null).Select(o => o.OData)
            .ShouldBeEmpty("every option counts on the ranks of its column");
        var watch = Stopwatch.StartNew();
        string[] sorts = ["Total desc", "OrderDate desc", "Customer/Name", "Number desc", "Items desc"];
        foreach (var sort in sorts)
        {
            watch.Restart();
            var rows = table.Find($"Number ne '{Guid.NewGuid():N}'", sort);
            var find = watch.Elapsed.TotalMilliseconds;
            watch.Restart();
            var counted = LocalFacets.Count(facets, table, rows);
            output.WriteLine($"{sort}: find {find:0.0} ms, facets {watch.Elapsed.TotalMilliseconds:0.0} ms");
            counted.Single(g => g.Key == "region").Options.Sum(o => o.Count).ShouldBe(Rows);
            // generous: catches a pass per row and option, not a slow machine
            watch.Elapsed.TotalMilliseconds.ShouldBeLessThan(500);
        }

        var all = LocalFacets.Count(facets, table, table.Find(null, null)).SelectMany(g => g.Options).ToDictionary(o => o.OData, o => o.Count);
        foreach (var option in facets.SelectMany(g => g.Options).Where((_, i) => i % 7 == 0))
        {
            all.GetValueOrDefault(option.OData).ShouldBe(orders.Count(ODataLinq.Filter<Order>(option.OData).Compile()), option.OData);
        }

        foreach (var group in facets)
        {
            var rows = table.Find(null, null);
            watch.Restart();
            for (var i = 0; i < 5; i++)
            {
                LocalFacets.Count([group], table, rows);
            }

            output.WriteLine($"  facet {group.Key}: {watch.Elapsed.TotalMilliseconds / 5:0.00} ms");
        }

        await Task.CompletedTask;
    }
}
