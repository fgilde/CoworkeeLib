using System.Text.Json;
using Bunit;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;
using Coworkee.Contracts.Settings;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;
using static Coworkee.Client.Blazor.Tests.DataTableStateTests;

namespace Coworkee.Client.Blazor.Tests;

public sealed class DataTableLayoutTests : ClientTestBase
{
    private readonly FakeODataClient _odata = new();
    private readonly List<ODataQuery> _queries = [];
    private readonly IRenderedComponent<MudPopoverProvider> _popovers;

    public DataTableLayoutTests()
    {
        _odata.With("Gadgets", new Gadget(Guid.CreateVersion7(), "Drill", "Tools", 3), new Gadget(Guid.CreateVersion7(), "Lamp", "Light", 1),
            new Gadget(Guid.CreateVersion7(), "Saw", "Tools", 2));
        _odata.Queried += (_, query) => _queries.Add(query);
        Services.AddSingleton<IODataClient>(_odata);
        AddAuthorization().SetAuthorized("Ada");
        _popovers = Render<MudPopoverProvider>();
        Render<MudSnackbarProvider>();
    }

    [Fact]
    public async Task The_column_chooser_hides_shows_and_reorders_columns()
    {
        var table = RenderTable();

        await SetColumnVisibleAsync(table, _popovers, "Category", false);
        table.WaitForAssertion(() => Headers(table).ShouldBe(["Name", "Stock"]));
        await _popovers.Find("[data-testid='columns-none']").ClickAsync(new());
        table.WaitForAssertion(() => table.Instance.CurrentState.HiddenColumns.ShouldBe(["Name", "Category", "Stock"]));
        await _popovers.Find("[data-testid='columns-all']").ClickAsync(new());
        table.WaitForAssertion(() => Headers(table).ShouldBe(["Name", "Category", "Stock"]));

        var chooser = table.FindComponent<DataTableColumnChooser<Gadget>>();
        var stock = Grid(table).RenderedColumns.Single(c => c.PropertyName == "Stock");
        await table.InvokeAsync(() => chooser.Instance.DroppedAsync(new MudItemDropInfo<Column<Gadget>>(stock, "columns", 0)));

        table.WaitForAssertion(() => Headers(table).ShouldBe(["Stock", "Name", "Category"]));
        table.Instance.CurrentState.ColumnOrder.ShouldBe(["Stock", "Name", "Category"]);
    }

    [Fact]
    public async Task Grouping_shows_group_rows_sorts_the_server_query_by_the_groups_and_can_be_removed()
    {
        var table = RenderTable();

        await OpenAsync(table, _popovers, "grouping", "[data-group='Category']");
        await _popovers.Find("[data-group='Category']").ClickAsync(new());

        table.WaitForAssertion(() => table.FindAll("[data-group-key]").Select(g => g.TextContent).ShouldBe(["Tools", "Light"]));
        table.WaitForAssertion(() => _queries.Last().OrderBy.ShouldBe("Category asc"));
        table.Find("[data-group-chip='Category']").TextContent.ShouldContain("Category");
        table.Instance.CurrentState.GroupBy.ShouldBe(["Category"]);

        await table.Find("[data-testid='group-zone-clear']").ClickAsync(new());

        table.WaitForAssertion(() => table.FindAll("[data-group-key]").ShouldBeEmpty());
        table.Instance.CurrentState.GroupBy.ShouldBeNull();
    }

    [Fact]
    public async Task A_column_header_dropped_on_the_group_zone_groups_by_it()
    {
        var table = RenderTable();
        var headers = table.FindComponents<MudDropContainer<Column<Gadget>>>().First(c => c.Instance.Items.Count() == 3);
        var category = Grid(table).RenderedColumns.Single(c => c.PropertyName == "Category");

        await table.InvokeAsync(() => headers.Instance.ItemDropped.InvokeAsync(new MudItemDropInfo<Column<Gadget>>(category, DataTableGroupZone<Gadget>.ZoneId, 0)));
        headers.Render();

        table.WaitForAssertion(() => table.Instance.CurrentState.GroupBy.ShouldBe(["Category"]));
        table.WaitForAssertion(() => table.FindAll("[data-group-key]").Count.ShouldBe(2));
    }

    [Fact]
    public async Task A_saved_view_brings_back_grouping_order_hidden_columns_sort_and_density()
    {
        var table = RenderTable();
        var grid = Grid(table);
        await table.InvokeAsync(async () =>
        {
            await table.FindComponent<DataTableGroupMenu<Gadget>>().Instance.DroppedAsync(new MudItemDropInfo<Column<Gadget>>(grid.RenderedColumns.Single(c => c.PropertyName == "Category"), "grouped", 0));
            await table.FindComponent<DataTableColumnChooser<Gadget>>().Instance.DroppedAsync(new MudItemDropInfo<Column<Gadget>>(grid.RenderedColumns.Single(c => c.PropertyName == "Stock"), "columns", 0));
            await grid.SetSortAsync("Name", MudBlazor.SortDirection.Descending, g => g.Name);
        });
        await table.Find("[data-testid='density']").ClickAsync(new());
        await SetColumnVisibleAsync(table, _popovers, "Name", false);
        var saved = table.Instance.CurrentState;
        saved.GroupBy.ShouldBe(["Category"]);
        saved.ColumnOrder.ShouldBe(["Stock", "Name", "Category"]);
        saved.HiddenColumns.ShouldBe(["Name"]);
        saved.Sort.ShouldBe([new DataTableSort("Name", true)]);
        saved.Dense.ShouldBe(false);

        await OpenAsync(table, _popovers, "views", "[data-testid='save-view']");
        _popovers.Find("input[data-testid='view-name'], [data-testid='view-name'] input").Input("Stock");
        await _popovers.Find("[data-testid='save-view']").ClickAsync(new());
        await _popovers.Find("[data-testid='reset-view']").ClickAsync(new());
        table.WaitForAssertion(() => table.Instance.CurrentState.IsEmpty.ShouldBeTrue());
        Headers(table).ShouldBe(["Name", "Category", "Stock"]);

        await _popovers.Find("[data-view='Stock']").ClickAsync(new());

        table.WaitForAssertion(() => table.Instance.CurrentState.Encode().ShouldBe(saved.Encode()));
        table.WaitForAssertion(() => Headers(table).ShouldBe(["Stock", "Category"]));
        _queries.Last().OrderBy.ShouldBe("Category asc,Name desc");
    }

    [Fact]
    public async Task Views_are_kept_in_the_user_setting_and_the_default_view_opens_with_the_table()
    {
        Api.GetSettingsAsync(SettingScope.User).Returns([new SettingValueDto(TableSettings.Views, null, false)]);
        var table = RenderTable();
        await OpenAsync(table, _popovers, "grouping", "[data-group='Category']");
        await _popovers.Find("[data-group='Category']").ClickAsync(new());
        await OpenAsync(table, _popovers, "views", "[data-testid='save-view']");
        _popovers.Find("input[data-testid='view-name'], [data-testid='view-name'] input").Input("By category");
        await _popovers.Find("[data-testid='view-default']").ClickAsync(new());
        await _popovers.Find("[data-testid='save-view']").ClickAsync(new());

        var json = (string)Api.ReceivedCalls().Last(c => c.GetMethodInfo().Name == nameof(Api.SetSettingsAsync)).GetArguments()[1]!
            .ShouldBeAssignableTo<IReadOnlyDictionary<string, string?>>()![TableSettings.Views]!;
        var views = JsonSerializer.Deserialize<Dictionary<string, List<SavedTableView>>>(json, JsonSerializerOptions.Web)!["Gadgets"];
        (views.Single().Name, views.Single().IsDefault).ShouldBe(("By category", true));
        views.Single().State.GroupBy.ShouldBe(["Category"]);

        Api.GetSettingsAsync(SettingScope.User).Returns([new SettingValueDto(TableSettings.Views, json, true)]);
        var stored = Services.GetRequiredService<ITableViewStore>();
        (await stored.GetAsync("Gadgets")).Single().IsDefault.ShouldBeTrue();
        var reopened = RenderTable();

        reopened.WaitForAssertion(() => reopened.Instance.CurrentState.GroupBy.ShouldBe(["Category"]));
        reopened.WaitForAssertion(() => reopened.FindAll("[data-group-key]").Count.ShouldBe(2));
    }

    [Fact]
    public void A_shared_link_opens_the_table_with_its_layout()
    {
        var state = DataTableState.Empty with { GroupBy = ["Category"], ColumnOrder = ["Category", "Name", "Stock"], PageSize = 10 };
        DataTableState.Decode(state.Encode())!.Encode().ShouldBe(state.Encode());
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/gadgets?gadgets={state.Encode()}");

        var table = RenderTable();

        table.WaitForAssertion(() => Headers(table).ShouldBe(["Category", "Name", "Stock"]));
        table.WaitForAssertion(() => (_queries.Last().Top, _queries.Last().OrderBy).ShouldBe((10, "Category asc")));
        table.Instance.CurrentState.Encode().ShouldBe(state.Encode());
    }

    private IRenderedComponent<CoworkeeDataTable<Gadget>> RenderTable()
    {
        var table = Render<CoworkeeDataTable<Gadget>>(p => p.Add(t => t.EntitySet, "Gadgets").Add(t => t.Columns, Columns()));
        table.WaitForAssertion(() => table.Markup.ShouldContain("Drill"));
        table.WaitForAssertion(() => table.FindAll("[data-testid='grouping']").ShouldNotBeEmpty());
        return table;
    }

    private static MudDataGrid<Gadget> Grid(IRenderedComponent<CoworkeeDataTable<Gadget>> table) => table.FindComponent<MudDataGrid<Gadget>>().Instance;

    private static IReadOnlyList<string> Headers(IRenderedComponent<CoworkeeDataTable<Gadget>> table) =>
        table.FindAll("th.mud-table-cell .sortable-column-header").Select(h => h.TextContent.Trim()).Where(t => t.Length > 0).ToList();

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
