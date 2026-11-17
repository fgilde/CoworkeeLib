using Bunit;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Coworkee.Client.Blazor.Tests;

/// <summary>The extension points for a page's own list look: chooser, views menu, own tool bar, bindings, action column, cards.</summary>
public sealed class DataTableExtensionTests : ClientTestBase
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);
    private static readonly FacetGroupDto Category = new("category", "Category", "Category", FacetKind.CheckboxList, FacetOperator.Or,
        [new(System.Text.Json.JsonSerializer.SerializeToElement("Tools"), "Tools", true, 2, "Category eq 'Tools'")], true, 0);

    private static readonly Gadget[] Rows =
    [
        new(Guid.CreateVersion7(), "Drill", "Tools", 3), new(Guid.CreateVersion7(), "Lamp", "Light", 1), new(Guid.CreateVersion7(), "Saw", "Tools", 2),
        new(Guid.CreateVersion7(), "Vise", "Tools", 5), new(Guid.CreateVersion7(), "Bulb", "Light", 9),
    ];

    private readonly PagingODataClient<Gadget> _odata = new(Rows, Category);
    private readonly IRenderedComponent<MudPopoverProvider> _popovers;

    public DataTableExtensionTests()
    {
        Services.AddSingleton<IODataClient>(_odata);
        AddAuthorization().SetAuthorized("Ada");
        _popovers = Render<MudPopoverProvider>();
        Render<MudSnackbarProvider>();
    }

    private List<ODataQuery> RowQueries
    {
        get
        {
            lock (_odata.Queries)
            {
                return [.. _odata.Queries.Where(q => !q.Facets && q.Top is > 1)];
            }
        }
    }

    [Fact]
    public void Without_the_new_parameters_the_table_keeps_its_tool_bar_menus_action_column_and_rows()
    {
        var table = RenderTable(p => p.Add(t => t.RowHref, g => $"/gadgets/{g.Id}"));

        table.FindAll("[data-testid='table-search']").ShouldBeEmpty("no search fields declared");
        table.Find("[data-testid='columns']");
        table.Find("[data-testid='views']");
        table.FindAll("[data-testid='open-row']").Count.ShouldBe(5);
        table.FindAll("[data-testid='table-cards']").ShouldBeEmpty();
        table.Find("[data-testid='data-table']").ClassList.ShouldNotContain("cw-table-hidden");
        _popovers.FindAll("[data-testid='column-sort']").ShouldBeEmpty();
    }

    [Fact]
    public async Task An_own_column_chooser_replaces_the_menu_lists_the_columns_and_applies_them()
    {
        RenderFragment<CoworkeeDataTable<Gadget>> chooser = t => builder =>
        {
            builder.OpenElement(0, "button");
            builder.AddAttribute(1, "data-testid", "my-chooser");
            builder.AddAttribute(2, "data-columns", string.Join(",", t.LayoutColumns.Select(c => $"{c.Key}:{c.Visible}")));
            builder.AddAttribute(3, "onclick", EventCallback.Factory.Create(this, () => t.SetColumnsAsync(["Stock", "Name"])));
            builder.CloseElement();
        };
        var table = RenderTable(p => p.Add(t => t.ColumnChooserContent, chooser));
        table.FindAll("[data-testid='columns']").ShouldBeEmpty();
        table.WaitForAssertion(() => table.Find("[data-testid='my-chooser']").GetAttribute("data-columns").ShouldBe("Name:True,Category:True,Stock:True"), Wait);

        await ClickAsync(table, "[data-testid='my-chooser']");

        table.WaitForAssertion(() => Headers(table).ShouldBe(["Stock", "Name"]), Wait);
        table.Instance.CurrentState.ColumnOrder.ShouldBe(["Stock", "Name", "Category"]);
        table.Instance.CurrentState.HiddenColumns.ShouldBe(["Category"]);
        table.Instance.LayoutColumns.Select(c => (c.Key, c.Visible)).ShouldBe([("Stock", true), ("Name", true), ("Category", false)]);
    }

    [Fact]
    public async Task An_own_views_menu_replaces_the_bookmark_menu_and_applies_a_state()
    {
        RenderFragment<CoworkeeDataTable<Gadget>> menu = t => builder =>
        {
            builder.OpenElement(0, "button");
            builder.AddAttribute(1, "data-testid", "my-views");
            builder.AddAttribute(2, "onclick", EventCallback.Factory.Create(this, () => t.ApplyStateAsync(
                DataTableState.Empty with { HiddenColumns = ["Stock"], Sort = [new DataTableSort("Name", true)], ColumnOrder = ["Category", "Name", "Stock"] })));
            builder.CloseElement();
        };
        var table = RenderTable(p => p.Add(t => t.ViewMenuContent, menu));
        table.FindAll("[data-testid='views']").ShouldBeEmpty();

        await ClickAsync(table, "[data-testid='my-views']");

        table.WaitForAssertion(() => Headers(table).ShouldBe(["Category", "Name"]), Wait);
        table.WaitForAssertion(() => RowQueries.Last().OrderBy.ShouldBe("Name desc"), Wait);
        table.Instance.CurrentState.HiddenColumns.ShouldBe(["Stock"]);

        await table.InvokeAsync(() => table.Instance.ApplyStateAsync(DataTableState.Empty));
        table.WaitForAssertion(() => Headers(table).ShouldBe(["Name", "Category", "Stock"]), Wait);
        table.Instance.CurrentState.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public async Task A_column_header_dragged_to_another_place_reorders_the_columns_in_the_state()
    {
        var table = RenderTable();
        var headers = table.FindComponents<MudDropContainer<Column<Gadget>>>().First(c => c.Instance.Items.Count() == 3);
        var zone = table.FindComponents<MudDropZone<Column<Gadget>>>().First(z => z.Instance.Identifier != DataTableGroupZone<Gadget>.ZoneId).Instance.Identifier;
        var stock = Grid(table).RenderedColumns.Single(c => c.PropertyName == "Stock");

        await table.InvokeAsync(() => headers.Instance.ItemDropped.InvokeAsync(new MudItemDropInfo<Column<Gadget>>(stock, zone, 0)));
        headers.Render();

        table.WaitForAssertion(() => table.Instance.CurrentState.ColumnOrder.ShouldBe(["Stock", "Name", "Category"]), Wait);
        table.WaitForAssertion(() => Headers(table).ShouldBe(["Stock", "Name", "Category"]), Wait);
    }

    [Fact]
    public async Task Search_and_sort_bind_from_outside_and_header_sorts_go_back()
    {
        var searched = new List<string?>();
        var sorted = new List<IReadOnlyList<DataTableSort>>();
        var table = RenderTable(p => p.Add(t => t.SearchFields, ["Name"]).Add(t => t.SearchText, "dri").Add(t => t.SearchTextChanged, s => searched.Add(s))
            .Add(t => t.Sort, [new DataTableSort("Name", true)]).Add(t => t.SortChanged, s => sorted.Add(s)));

        table.WaitForAssertion(() => (RowQueries.Last().Filter, RowQueries.Last().OrderBy).ShouldBe(("contains(tolower(Name),'dri')", "Name desc")), Wait);

        table.Render(p => p.Add(t => t.SearchText, "saw").Add(t => t.Sort, [new DataTableSort("Stock", false)]));

        table.WaitForAssertion(() => (RowQueries.Last().Filter, RowQueries.Last().OrderBy).ShouldBe(("contains(tolower(Name),'saw')", "Stock asc")), Wait);
        searched.ShouldBeEmpty("the page set both itself");

        table.Find("input[data-testid='table-search'], [data-testid='table-search'] input").Input("vise");
        table.WaitForAssertion(() => searched.ShouldBe(["vise"]), Wait);
        await table.InvokeAsync(() => Grid(table).SetSortAsync("Category", MudBlazor.SortDirection.Descending, g => g.Category));
        table.WaitForAssertion(() => sorted.Last().ShouldBe([new DataTableSort("Category", true)]), Wait);
    }

    [Fact]
    public void Without_the_tool_bar_the_page_gets_the_facets_and_the_total_for_its_own_filter_bar()
    {
        IReadOnlyList<FacetGroupDto>? loaded = null;
        long total = 0;
        var table = RenderTable(p => p.Add(t => t.Toolbar, false).Add(t => t.FacetsLoaded, g => loaded = g).Add(t => t.TotalCountChanged, c => total = c));

        table.FindAll("[data-testid='columns'], [data-testid='views'], [data-testid='export'], [data-testid='facet-chips']").ShouldBeEmpty();
        table.WaitForAssertion(() => loaded.ShouldNotBeNull().Single().Key.ShouldBe("category"), Wait);
        table.Instance.FacetGroups.ShouldBeSameAs(loaded);
        table.WaitForAssertion(() => total.ShouldBe(5), Wait);
        table.Instance.TotalCount.ShouldBe(5);
    }

    [Fact]
    public async Task Without_row_actions_rows_still_open_by_click_but_have_no_action_column()
    {
        var table = RenderTable(p => p.Add(t => t.RowHref, g => $"/gadgets/{g.Id}").Add(t => t.RowActions, false));

        table.FindAll("[data-testid='open-row']").ShouldBeEmpty();
        table.FindAll("th.cw-row-actions").ShouldBeEmpty();
        await table.InvokeAsync(() => table.FindAll("tbody tr.cw-clickable")[1].QuerySelector("td")!.Click());

        table.WaitForAssertion(() => Services.GetRequiredService<NavigationManager>().Uri.ShouldEndWith($"/gadgets/{Rows[1].Id}"), Wait);
    }

    [Fact]
    public async Task Cards_show_the_rows_page_by_page_with_the_tables_query_and_switch_back_to_rows()
    {
        RenderFragment<Gadget> card = g => builder =>
        {
            builder.OpenElement(0, "b");
            builder.AddContent(1, g.Name);
            builder.CloseElement();
        };
        var table = Render<CoworkeeDataTable<Gadget>>(p => p.Add(t => t.EntitySet, "Gadgets").Add(t => t.Columns, Columns()).Add(t => t.PageSize, 2)
            .Add(t => t.ItemTemplate, card).Add(t => t.View, DataTableView.Cards).Add(t => t.RowHref, g => $"/gadgets/{g.Id}"));

        table.WaitForAssertion(() => Cards(table).ShouldBe(["Drill", "Lamp"]), Wait);
        table.Find("[data-testid='data-table']").ClassList.ShouldContain("cw-table-hidden");
        await table.InvokeAsync(() => table.Find("[data-testid='cards-more']").Click());
        table.WaitForAssertion(() => Cards(table).ShouldBe(["Drill", "Lamp", "Saw", "Vise"]), Wait);
        await table.InvokeAsync(() => table.Find("[data-testid='cards-more']").Click());
        table.WaitForAssertion(() => Cards(table).Count.ShouldBe(5), Wait);
        table.FindAll("[data-testid='cards-more']").ShouldBeEmpty();
        RowQueries.Select(q => (q.Skip, q.Top)).ShouldBe([(0, 2), (2, 2), (4, 2)]);

        await table.InvokeAsync(() => table.FindAll("[data-testid='table-card']")[2].Click());
        table.WaitForAssertion(() => Services.GetRequiredService<NavigationManager>().Uri.ShouldEndWith($"/gadgets/{Rows[2].Id}"), Wait);

        table.Render(p => p.Add(t => t.View, DataTableView.Table));
        table.WaitForAssertion(() => table.FindAll("tbody tr.mud-table-row").Count.ShouldBe(2), Wait);
        table.FindAll("[data-testid='table-cards']").ShouldBeEmpty();
        table.Find("[data-testid='data-table']").ClassList.ShouldNotContain("cw-table-hidden");
    }

    [Fact]
    public async Task Endless_rows_switched_to_cards_load_the_first_page_of_cards_at_once()
    {
        RenderFragment<Gadget> card = g => builder =>
        {
            builder.OpenElement(0, "b");
            builder.AddContent(1, g.Name);
            builder.CloseElement();
        };
        var table = Render<CoworkeeDataTable<Gadget>>(p => p.Add(t => t.EntitySet, "Gadgets").Add(t => t.Columns, Columns()).Add(t => t.PageSize, 2)
            .Add(t => t.ItemTemplate, card).Add(t => t.PageMode, DataTablePageMode.EndlessScroll));
        table.WaitForAssertion(() => table.Markup.ShouldContain("Drill"), Wait);
        var before = RowQueries.Count;

        table.Render(p => p.Add(t => t.View, DataTableView.Cards));

        // the hidden grid still holds the scrolling loader in this render: the cards ask for their first page themselves, once
        table.WaitForAssertion(() => Cards(table).ShouldBe(["Drill", "Lamp"]), Wait);
        RowQueries.Skip(before).Count(q => q.Skip == 0 && q.Top == 2).ShouldBe(1);
        await table.InvokeAsync(() => table.Instance.ReloadAsync());
        table.WaitForAssertion(() => Cards(table).ShouldBe(["Drill", "Lamp"]), Wait);

        table.Render(p => p.Add(t => t.View, DataTableView.Table));
        table.WaitForAssertion(() => table.FindAll("tbody tr.mud-table-row").Count.ShouldBeGreaterThan(0), Wait);
        table.FindAll("[data-testid='table-cards']").ShouldBeEmpty();
    }

    private IRenderedComponent<CoworkeeDataTable<Gadget>> RenderTable(Action<ComponentParameterCollectionBuilder<CoworkeeDataTable<Gadget>>>? parameters = null)
    {
        var table = Render<CoworkeeDataTable<Gadget>>(p =>
        {
            p.Add(t => t.EntitySet, "Gadgets").Add(t => t.Columns, Columns()).Add(t => t.PageSize, 10);
            parameters?.Invoke(p);
        });
        table.WaitForAssertion(() => table.Markup.ShouldContain("Drill"), Wait);
        // the facets come after the rows and render the table once more
        table.WaitForAssertion(() => table.Instance.FacetGroups.ShouldNotBeEmpty(), Wait);
        if (table.Instance.Toolbar)
        {
            table.WaitForElement("[data-testid='facet-button']", Wait);
        }
        return table;
    }

    private static Task ClickAsync(IRenderedComponent<CoworkeeDataTable<Gadget>> table, string selector) => table.InvokeAsync(() => table.Find(selector).ClickAsync(new()));

    private static MudDataGrid<Gadget> Grid(IRenderedComponent<CoworkeeDataTable<Gadget>> table) => table.FindComponent<MudDataGrid<Gadget>>().Instance;

    private static IReadOnlyList<string> Headers(IRenderedComponent<CoworkeeDataTable<Gadget>> table) =>
        table.FindAll("th.mud-table-cell .sortable-column-header").Select(h => h.TextContent.Trim()).Where(t => t.Length > 0).ToList();

    private static List<string> Cards(IRenderedComponent<CoworkeeDataTable<Gadget>> table) => [.. table.FindAll("[data-testid='table-card'] b").Select(b => b.TextContent)];

    private static RenderFragment Columns() => builder =>
    {
        builder.OpenComponent<PropertyColumn<Gadget, string>>(0);
        builder.AddComponentParameter(1, nameof(PropertyColumn<Gadget, string>.Property), (System.Linq.Expressions.Expression<Func<Gadget, string>>)(g => g.Name));
        builder.CloseComponent();
        builder.OpenComponent<PropertyColumn<Gadget, string>>(2);
        builder.AddComponentParameter(3, nameof(PropertyColumn<Gadget, string>.Property), (System.Linq.Expressions.Expression<Func<Gadget, string>>)(g => g.Category));
        builder.CloseComponent();
        builder.OpenComponent<PropertyColumn<Gadget, int>>(4);
        builder.AddComponentParameter(5, nameof(PropertyColumn<Gadget, int>.Property), (System.Linq.Expressions.Expression<Func<Gadget, int>>)(g => g.Stock));
        builder.CloseComponent();
    };

    public sealed record Gadget(Guid Id, string Name, string Category, int Stock);
}
