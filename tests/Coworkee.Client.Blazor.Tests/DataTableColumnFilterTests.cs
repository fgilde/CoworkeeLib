using System.Text.Json;
using AngleSharp.Dom;
using Bunit;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Coworkee.Client.Blazor.Tests;

public sealed class DataTableColumnFilterTests : ClientTestBase
{
    private static readonly FacetGroupDto Category = new("category", "Category", "Category", FacetKind.CheckboxList, FacetOperator.Or,
        [new(JsonSerializer.SerializeToElement("Tools"), "Tools", true, 2, "Category eq 'Tools'"), new(JsonSerializer.SerializeToElement("Light"), "Light", true, 1, "Category eq 'Light'")],
        true, 0);

    private readonly FakeODataClient _odata = new();
    private readonly List<ODataQuery> _queries = [];
    private readonly IRenderedComponent<MudPopoverProvider> _popovers;

    public DataTableColumnFilterTests()
    {
        _odata.With("Devices", new Device("Drill", "Tools", 30m, Kind.Power, new DateTime(2026, 3, 1)), new Device("Lamp", "Light", 12.5m, Kind.Hand, new DateTime(2026, 1, 5)))
            .WithFacets(Category);
        _odata.Queried += (_, query) => _queries.Add(query);
        Services.AddSingleton<IODataClient>(_odata);
        AddAuthorization().SetAuthorized("Ada");
        _popovers = Render<MudPopoverProvider>();
        Render<MudSnackbarProvider>();
    }

    public enum Kind
    {
        Hand,
        Power,
    }

    [Fact]
    public async Task A_text_filter_becomes_an_odata_contains_a_chip_and_part_of_the_state()
    {
        var table = RenderTable();

        await OpenFilterAsync(table, "Name");
        Input("filter-value").Input("Dri");
        await _popovers.Find("[data-testid='filter-apply']").ClickAsync(new());

        table.WaitForAssertion(() => _queries.Last().Filter.ShouldBe("contains(tolower(Name),'dri')"));
        table.Find("[data-filter-chip='Name']").TextContent.ShouldContain("Dri");
        Header(table, "Name").QuerySelector(".filter-button.filtered").ShouldNotBeNull();
        Table(table).CurrentState.ColumnFilters.ShouldBe([new DataTableFilter("Name", FilterOperator.String.Contains, "Dri")]);

        await table.Find("[data-filter-chip='Name'] .mud-chip-close-button").ClickAsync(new());

        table.WaitForAssertion(() => _queries.Last().Filter.ShouldBeNull());
        Table(table).CurrentState.ColumnFilters.ShouldBeNull();
    }

    [Fact]
    public async Task Numbers_filter_between_two_values_and_enums_by_several_choices()
    {
        var table = RenderTable();

        await OpenFilterAsync(table, "Price");
        await _popovers.Find($"[data-operator='{DataTableFilters.Between}']").ClickAsync(new());
        Input("filter-value").Change("10");
        Input("filter-to").Change("20.5");
        await _popovers.Find("[data-testid='filter-apply']").ClickAsync(new());
        table.WaitForAssertion(() => _queries.Last().Filter.ShouldBe("(Price ge 10) and (Price le 20.5)"));

        await OpenFilterAsync(table, "Kind");
        await _popovers.Find("input[data-choice='Hand'], [data-choice='Hand'] input").ChangeAsync(new() { Value = true });
        await _popovers.Find("input[data-choice='Power'], [data-choice='Power'] input").ChangeAsync(new() { Value = true });
        await _popovers.Find("[data-testid='filter-apply']").ClickAsync(new());

        table.WaitForAssertion(() => _queries.Last().Filter.ShouldBe("(Price ge 10) and (Price le 20.5) and (Kind eq 'Hand' or Kind eq 'Power')"));
        table.Find("[data-filter-chip='Kind']").TextContent.ShouldContain("Hand, Power");

        await table.Find("[data-testid='clear-facets']").ClickAsync(new());

        table.WaitForAssertion(() => _queries.Last().Filter.ShouldBeNull());
        table.FindAll("[data-filter-chip]").ShouldBeEmpty();
    }

    [Fact]
    public async Task A_column_with_a_facet_filters_with_the_facet_options()
    {
        var table = RenderTable();

        await OpenFilterAsync(table, "Category");
        _popovers.Find("[data-testid='column-filter']").TextContent.ShouldContain("Tools");
        _popovers.FindAll("[data-testid='filter-apply']").ShouldBeEmpty();
        await _popovers.Find("[data-option=\"Category eq 'Tools'\"] input").ChangeAsync(new() { Value = true });

        table.WaitForAssertion(() => _queries.Last().Filter.ShouldBe("Category eq 'Tools'"));
        table.WaitForAssertion(() => Header(table, "Category").QuerySelector(".filter-button.filtered").ShouldNotBeNull());
        table.Find("[data-chip=\"Category eq 'Tools'\"]").ShouldNotBeNull();
        table.FindAll("[data-filter-chip]").ShouldBeEmpty();
        Table(table).CurrentState.ColumnFilters.ShouldBeNull();
    }

    [Fact]
    public async Task Column_filters_come_back_with_a_saved_view_and_a_shared_link()
    {
        var table = RenderTable();
        await OpenFilterAsync(table, "Added");
        await _popovers.Find($"[data-operator='{FilterOperator.DateTime.After}']").ClickAsync(new());
        Input("filter-value").Change(new DateTime(2026, 2, 1).ToShortDateString());
        await _popovers.Find("[data-testid='filter-apply']").ClickAsync(new());
        table.WaitForAssertion(() => _queries.Last().Filter.ShouldStartWith("Added ge 2026-02-02T00:00:00"));
        var saved = Table(table).CurrentState;

        await DataTableStateTests.OpenAsync(table, _popovers, "views", "[data-testid='save-view']");
        _popovers.Find("input[data-testid='view-name'], [data-testid='view-name'] input").Input("Recent");
        await _popovers.Find("[data-testid='save-view']").ClickAsync(new());
        await _popovers.Find("[data-testid='reset-view']").ClickAsync(new());
        table.WaitForAssertion(() => _queries.Last().Filter.ShouldBeNull());
        Header(table, "Added").QuerySelector(".filter-button.filtered").ShouldBeNull();

        await _popovers.Find("[data-view='Recent']").ClickAsync(new());
        table.WaitForAssertion(() => _queries.Last().Filter.ShouldStartWith("Added ge 2026-02-02T00:00:00"));
        Table(table).CurrentState.Encode().ShouldBe(saved.Encode());

        Services.GetRequiredService<NavigationManager>().NavigateTo($"/devices?devices={saved.Encode()}");
        var shared = RenderTable();
        shared.WaitForAssertion(() => _queries.Last().Filter.ShouldStartWith("Added ge 2026-02-02T00:00:00"));
        Table(shared).CurrentState.ColumnFilters.ShouldBe(saved.ColumnFilters);
    }

    [Fact]
    public void Filters_can_be_turned_off_per_table_type_and_column_and_on_per_column()
    {
        Services.GetRequiredService<CoworkeeClientOptions>().DataTable.FilterOperators<decimal>();

        var table = RenderTable(p => p.Add(t => t.NameFilterable, false));

        Header(table, "Name").QuerySelector(".filter-button").ShouldBeNull();
        Header(table, "Price").QuerySelector(".filter-button").ShouldBeNull();
        Header(table, "Actions").QuerySelector(".filter-button").ShouldBeNull();
        Header(table, "Kind").QuerySelector(".filter-button").ShouldNotBeNull();

        var off = RenderTable(p => p.Add(t => t.ColumnFilters, false).Add(t => t.NameFilterable, true));

        off.FindAll(".filter-button").Count.ShouldBe(1);
        Header(off, "Name").QuerySelector(".filter-button").ShouldNotBeNull();
    }

    private IRenderedComponent<DeviceTable> RenderTable(Action<ComponentParameterCollectionBuilder<DeviceTable>>? parameters = null)
    {
        var table = Render<DeviceTable>(p => parameters?.Invoke(p));
        table.WaitForAssertion(() => table.Markup.ShouldContain("Drill"));
        table.WaitForAssertion(() => table.FindAll("[data-testid='columns']").ShouldNotBeEmpty());
        return table;
    }

    private static CoworkeeDataTable<Device> Table(IRenderedComponent<DeviceTable> table) => table.FindComponent<CoworkeeDataTable<Device>>().Instance;

    private static IElement Header(IRenderedComponent<DeviceTable> table, string title) =>
        table.FindAll("th.mud-table-cell").First(h => h.TextContent.Trim().StartsWith(title, StringComparison.Ordinal));

    private async Task OpenFilterAsync(IRenderedComponent<DeviceTable> table, string title)
    {
        await Header(table, title).QuerySelector(".filter-button")!.ClickAsync(new());
        _popovers.WaitForElement($"[data-testid='column-filter']");
    }

    private IElement Input(string testId) => _popovers.Find($"input[data-testid='{testId}'], [data-testid='{testId}'] input");

    public sealed record Device(string Name, string Category, decimal Price, Kind Kind, DateTime Added);
}
