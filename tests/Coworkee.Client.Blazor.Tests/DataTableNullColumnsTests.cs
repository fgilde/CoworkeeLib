using System.Linq.Expressions;
using Bunit;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Coworkee.Client.Blazor.Tests;

/// <summary>
/// Columns through navigations (<c>o =&gt; o.Status.Code</c>, <c>o =&gt; o.Supplier.Organization.Country.Code</c>) show an empty cell,
/// sort and group where a navigation on the way is null.
/// </summary>
public sealed class DataTableNullColumnsTests : ClientTestBase
{
    private static readonly Order[] Orders =
    [
        new(Guid.CreateVersion7(), "A-1", new Status("Open", 2), new Supplier(new Organization("Acme", 3, new Country("DE")))),
        new(Guid.CreateVersion7(), "A-2", null, null),
        new(Guid.CreateVersion7(), "A-3", new Status("Done", 7), new Supplier(null)),
        new(Guid.CreateVersion7(), "A-4", new Status("Open", 1), new Supplier(new Organization("Solo", 5, null))),
    ];

    public DataTableNullColumnsTests()
    {
        // FakeODataClient cannot group: a grouped table groups the loaded page through the columns
        Services.AddSingleton<IODataClient>(new FakeODataClient().With("Orders", Orders));
        AddAuthorization().SetAuthorized("Ada");
    }

    [Fact]
    public void A_null_navigation_renders_empty_cells()
    {
        var table = Table(DataTableState.Empty);

        table.WaitForAssertion(() => Cells(table, "A-2").ShouldBe(["A-2", "", "", "", "", "", ""]));
        Cells(table, "A-1").ShouldBe(["A-1", "Open", "2", "Acme", "3", "Country { Code = DE }", "DE"]);
        Cells(table, "A-3").ShouldBe(["A-3", "Done", "7", "", "", "", ""]);
        Cells(table, "A-4").ShouldBe(["A-4", "Open", "1", "Solo", "5", "", ""]);
    }

    [Fact]
    public void Rows_with_a_null_middle_navigation_render_again_after_the_table_renders_again()
    {
        var table = Table(DataTableState.Empty);
        table.WaitForAssertion(() => table.FindAll("tbody tr.mud-table-row").Count.ShouldBe(4));

        // every render brings new property expressions, which MudBlazor compiles into its cell function again
        table.Render();
        table.Render();

        Cells(table, "A-3").ShouldBe(["A-3", "Done", "7", "", "", "", ""]);
        var columns = table.FindComponent<MudDataGrid<Order>>().Instance.RenderedColumns;
        columns.Single(c => c.PropertyName == "Supplier.Organization.Name").GetCellContent(Orders[1]).ShouldBeNull();
        columns.Single(c => c.PropertyName == "Supplier.Organization.Rank").GetCellContent(Orders[2]).ShouldBeNull();
        columns.Single(c => c.PropertyName == "Supplier.Organization.Country.Code").GetCellContent(Orders[3]).ShouldBeNull();
        columns.Single(c => c.PropertyName == "Supplier.Organization.Country.Code").GetCellContent(Orders[0]).ShouldBe("DE");
    }

    [Fact]
    public void The_sort_function_of_a_column_through_a_null_navigation_answers_null()
    {
        var table = Table(DataTableState.Empty with { Sort = [new DataTableSort("Status.Code", false)] });
        table.WaitForAssertion(() => table.FindAll("tbody tr.mud-table-row").Count.ShouldBe(4));

        // MudBlazor sorts its filtered items (expand all rows, grids without pager) through these functions
        var columns = table.FindComponent<MudDataGrid<Order>>().Instance.RenderedColumns;
        columns.Single(c => c.PropertyName == "Status.Code").SortBy(Orders[1]).ShouldBeNull();
        columns.Single(c => c.PropertyName == "Status.Rank").SortBy(Orders[1]).ShouldBeNull();
        columns.Single(c => c.PropertyName == "Status.Rank").SortBy(Orders[2]).ShouldBe(7);
        columns.Single(c => c.PropertyName == "Supplier.Organization.Name").SortBy(Orders[1]).ShouldBeNull();
        columns.Single(c => c.PropertyName == "Supplier.Organization.Name").SortBy(Orders[2]).ShouldBeNull();
        columns.Single(c => c.PropertyName == "Supplier.Organization.Rank").SortBy(Orders[2]).ShouldBeNull();
        columns.Single(c => c.PropertyName == "Supplier.Organization.Rank").SortBy(Orders[0]).ShouldBe(3);
        columns.Single(c => c.PropertyName == "Supplier.Organization.Country").SortBy(Orders[3]).ShouldBeNull();
        columns.Single(c => c.PropertyName == "Supplier.Organization.Country.Code").SortBy(Orders[3]).ShouldBeNull();
    }

    [Fact]
    public void Sorting_by_a_deep_column_keeps_its_OData_path()
    {
        var queries = new List<ODataQuery>();
        var client = new FakeODataClient().With("Orders", Orders);
        client.Queried += (_, query) => queries.Add(query);
        Services.AddSingleton<IODataClient>(client);

        var table = Table(DataTableState.Empty with { Sort = [new DataTableSort("Supplier.Organization.Country.Code", true)] });

        table.WaitForAssertion(() => queries.ShouldContain(q => q.OrderBy == "Supplier/Organization/Country/Code desc"));
        Cells(table, "A-3").ShouldBe(["A-3", "Done", "7", "", "", "", ""]);
    }

    [Fact]
    public void Grouping_the_loaded_page_by_a_column_through_a_null_navigation_does_not_throw()
    {
        var table = Table(DataTableState.Empty with { GroupBy = ["Status.Code"] });

        table.WaitForAssertion(() => table.FindAll(".cw-group-title + strong").Select(g => g.TextContent.Trim()).OrderBy(t => t, StringComparer.Ordinal)
            .ShouldBe(["(empty)", "Done", "Open"]));
    }

    [Theory]
    [InlineData("Supplier.Organization.Name", new[] { "(empty)", "Acme", "Solo" })]
    [InlineData("Supplier.Organization.Rank", new[] { "(empty)", "3", "5" })]
    [InlineData("Supplier.Organization.Country.Code", new[] { "(empty)", "DE" })]
    public void Grouping_the_loaded_page_by_a_deep_column_with_null_navigations_does_not_throw(string column, string[] groups)
    {
        var table = Table(DataTableState.Empty with { GroupBy = [column] });

        table.WaitForAssertion(() => table.FindAll(".cw-group-title + strong").Select(g => g.TextContent.Trim()).OrderBy(t => t, StringComparer.Ordinal)
            .ShouldBe(groups));
    }

    [Fact]
    public void Exporting_rows_with_a_null_navigation_does_not_throw()
    {
        TableExport.ToCsv(Orders).ShouldContain("A-2");
        TableExport.ToJson(Orders).ShouldContain("\"status\": null");
        TableExport.ToJson(Orders).ShouldContain("\"organization\": null");
        TableExport.ToJson(Orders).ShouldContain("\"country\": null");
        TableExport.ToCsv(Orders).ShouldContain("A-4");
    }

    private IRenderedComponent<CoworkeeDataTable<Order>> Table(DataTableState state)
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo(nav.GetUriWithQueryParameter("orders", state.Encode()));
        return Render<CoworkeeDataTable<Order>>(p => p.Add(t => t.EntitySet, "Orders").Add(t => t.Columns, Columns()));
    }

    private static List<string> Cells(IRenderedComponent<CoworkeeDataTable<Order>> table, string number) =>
        [.. table.FindAll("tbody tr.mud-table-row").Single(r => r.QuerySelector("td")!.TextContent.Trim() == number)
            .QuerySelectorAll("td").Select(c => c.TextContent.Trim())];

    private static RenderFragment Columns() => builder =>
    {
        Column(builder, 0, o => o.Number);
        Column(builder, 2, o => o.Status!.Code);
        Column(builder, 4, o => o.Status!.Rank);
        Column(builder, 6, o => o.Supplier!.Organization!.Name);
        Column(builder, 8, o => o.Supplier!.Organization!.Rank);
        Column(builder, 10, o => o.Supplier!.Organization!.Country);
        Column(builder, 12, o => o.Supplier!.Organization!.Country!.Code);
    };

    private static void Column<TValue>(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder, int sequence, Expression<Func<Order, TValue>> property)
    {
        builder.OpenComponent<PropertyColumn<Order, TValue>>(sequence);
        builder.AddComponentParameter(sequence + 1, nameof(PropertyColumn<Order, TValue>.Property), property);
        builder.CloseComponent();
    }

    public sealed record Status(string Code, int Rank);

    public sealed record Country(string Code);

    public sealed record Organization(string Name, int Rank, Country? Country);

    public sealed record Supplier(Organization? Organization);

    public sealed record Order(Guid Id, string Number, Status? Status, Supplier? Supplier);
}
