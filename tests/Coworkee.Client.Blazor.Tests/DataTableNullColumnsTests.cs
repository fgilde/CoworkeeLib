using System.Linq.Expressions;
using Bunit;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Coworkee.Client.Blazor.Tests;

/// <summary>Columns through a navigation (<c>o =&gt; o.Status.Code</c>) show an empty cell, sort and group where the navigation is null.</summary>
public sealed class DataTableNullColumnsTests : ClientTestBase
{
    private static readonly Order[] Orders =
    [
        new(Guid.CreateVersion7(), "A-1", new Status("Open", 2)),
        new(Guid.CreateVersion7(), "A-2", null),
        new(Guid.CreateVersion7(), "A-3", new Status("Done", 7)),
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

        table.WaitForAssertion(() => Cells(table, "A-2").ShouldBe(["A-2", string.Empty, string.Empty]));
        Cells(table, "A-1").ShouldBe(["A-1", "Open", "2"]);
    }

    [Fact]
    public void The_sort_function_of_a_column_through_a_null_navigation_answers_null()
    {
        var table = Table(DataTableState.Empty with { Sort = [new DataTableSort("Status.Code", false)] });
        table.WaitForAssertion(() => table.FindAll("tbody tr.mud-table-row").Count.ShouldBe(3));

        // MudBlazor sorts its filtered items (expand all rows, grids without pager) through these functions
        var columns = table.FindComponent<MudDataGrid<Order>>().Instance.RenderedColumns;
        columns.Single(c => c.PropertyName == "Status.Code").SortBy(Orders[1]).ShouldBeNull();
        columns.Single(c => c.PropertyName == "Status.Rank").SortBy(Orders[1]).ShouldBeNull();
        columns.Single(c => c.PropertyName == "Status.Rank").SortBy(Orders[2]).ShouldBe(7);
    }

    [Fact]
    public void Grouping_the_loaded_page_by_a_column_through_a_null_navigation_does_not_throw()
    {
        var table = Table(DataTableState.Empty with { GroupBy = ["Status.Code"] });

        table.WaitForAssertion(() => table.FindAll(".cw-group-title + strong").Select(g => g.TextContent.Trim()).OrderBy(t => t, StringComparer.Ordinal)
            .ShouldBe(["(empty)", "Done", "Open"]));
    }

    [Fact]
    public void Exporting_rows_with_a_null_navigation_does_not_throw()
    {
        TableExport.ToCsv(Orders).ShouldContain("A-2");
        TableExport.ToJson(Orders).ShouldContain("\"status\": null");
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
    };

    private static void Column<TValue>(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder, int sequence, Expression<Func<Order, TValue>> property)
    {
        builder.OpenComponent<PropertyColumn<Order, TValue>>(sequence);
        builder.AddComponentParameter(sequence + 1, nameof(PropertyColumn<Order, TValue>.Property), property);
        builder.CloseComponent();
    }

    public sealed record Status(string Code, int Rank);

    public sealed record Order(Guid Id, string Number, Status? Status);
}
