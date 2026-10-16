using Coworkee.Application.Setup;
using Coworkee.Contracts.Identity;
#if (reports)
using Coworkee.Contracts.Reporting;
using Coworkee.Core.Security;
using Coworkee.Reporting;
#endif
using Coworkee.Core.Results;
using MyApp.Catalog.Domain;
using MyApp.Documents.Domain;
using MyApp.Infrastructure;

namespace MyApp.Migrations;

/// <summary>Sample catalog and document types, so a fresh demo shows filled tables, facets and charts.</summary>
#if (reports)
/// <remarks>The catalog report of the administrator is the organisation's dashboard.</remarks>
internal sealed class DemoData(MyAppDbContext db, ICurrentUser currentUser) : ISetupStep
#else
internal sealed class DemoData(MyAppDbContext db) : ISetupStep
#endif
{
    private static readonly (string Name, string Description, decimal Tax, (string Name, string Barcode, decimal Rate)[] Products)[] Catalog =
    [
        ("Nordwind", "Outdoor equipment", 19, [("Trail backpack", "4006381333931", 89.90m), ("Rain jacket", "4006381333948", 149m), ("Trekking poles", "4006381333955", 59.50m)]),
        ("Lumen", "Lights and lamps", 19, [("Desk lamp", "4006381333962", 39.99m), ("Head torch", "4006381333979", 24.90m)]),
        ("Kaffeerösterei Süd", "Coffee and tea", 7, [("Espresso beans 1 kg", "4006381333986", 18.50m), ("Green tea", "4006381333993", 6.90m), ("Filter coffee", "4006381334006", 9.90m)]),
        ("Werkbank", "Tools", 19, [("Cordless drill", "4006381334013", 129m), ("Hammer", "4006381334020", 19.90m), ("Screwdriver set", "4006381334037", 29.90m)]),
    ];

    private static readonly (string Name, string Description)[] DocumentTypes =
    [
        ("Invoice", "Incoming and outgoing invoices"),
        ("Contract", "Signed agreements"),
        ("Data sheet", "Product data sheets"),
    ];

    public Task<Error?> ApplyAsync(CompleteSetupRequest request, Guid tenantId, CancellationToken cancellationToken)
    {
        foreach (var (name, description, tax, products) in Catalog)
        {
            var brand = new Brand { Name = name, Description = description, Tax = tax, TenantId = tenantId };
            db.Add(brand);
            db.AddRange(products.Select(p => new Product { Name = p.Name, Barcode = p.Barcode, Rate = p.Rate, BrandId = brand.Id, TenantId = tenantId }));
        }

        db.AddRange(DocumentTypes.Select(t => new DocumentType { Name = t.Name, Description = t.Description, TenantId = tenantId }));
#if (reports)
        var report = new Report
        {
            TenantId = tenantId,
            OwnerId = currentUser.UserId ?? throw new InvalidOperationException("The setup runs as its administrator."),
            Name = "Catalog overview",
            Description = "Products and brands at a glance.",
            Visibility = ReportVisibility.Organisation,
            Definition = System.Text.Json.JsonSerializer.Serialize(CatalogOverview(), System.Text.Json.JsonSerializerOptions.Web),
        };
        db.Add(report);
        db.Add(new DashboardReport { TenantId = tenantId, ReportId = report.Id });
#endif
        return Task.FromResult<Error?>(null);
    }
#if (reports)

    private static ReportDefinition CatalogOverview() => new()
    {
        Widgets =
        [
            new ReportWidget
            {
                Id = "products", Type = WidgetType.Kpi, Title = "Products", X = 0, Y = 0, W = 4, H = 3,
                Query = new WidgetQuery { EntitySet = "Products", Aggregates = [new()] }, Options = new WidgetOptions { Format = NumberFormat.Integer },
            },
            new ReportWidget
            {
                Id = "brands", Type = WidgetType.Kpi, Title = "Brands", X = 4, Y = 0, W = 4, H = 3,
                Query = new WidgetQuery { EntitySet = "Brands", Aggregates = [new()] }, Options = new WidgetOptions { Format = NumberFormat.Integer },
            },
            new ReportWidget
            {
                Id = "rate", Type = WidgetType.Kpi, Title = "Average rate", X = 8, Y = 0, W = 4, H = 3,
                Query = new WidgetQuery { EntitySet = "Products", Aggregates = [new(AggregateFunction.Average, "Rate")] }, Options = new WidgetOptions { Format = NumberFormat.Currency },
            },
            new ReportWidget
            {
                Id = "per-brand", Type = WidgetType.Chart, Title = "Products per brand", X = 0, Y = 3, W = 6, H = 6,
                Query = new WidgetQuery { EntitySet = "Products", GroupBy = new ReportGroupBy("Brand/Name"), Aggregates = [new()], SortBy = ReportSortBy.Value, Descending = true },
                Options = new WidgetOptions { Chart = ChartKind.Bar, Format = NumberFormat.Integer },
            },
            new ReportWidget
            {
                Id = "latest", Type = WidgetType.Table, Title = "Latest products", X = 6, Y = 3, W = 6, H = 6,
                Query = new WidgetQuery { EntitySet = "Products", Columns = ["Name", "Brand/Name", "Rate", "CreatedAt"], OrderBy = "CreatedAt", Descending = true },
            },
        ],
    };
#endif
}
