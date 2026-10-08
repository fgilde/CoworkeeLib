using Bunit;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Coworkee.Client.Blazor.Tests;

public sealed class DataTableStateTests : ClientTestBase
{
    private readonly FakeODataClient _odata = new();
    private readonly List<ODataQuery> _queries = [];

    public DataTableStateTests()
    {
        _odata.With("Gadgets", new Gadget(Guid.CreateVersion7(), "Drill", "Tools"));
        _odata.Queried += (_, query) => _queries.Add(query);
        Services.AddSingleton<IODataClient>(_odata);
        AddAuthorization().SetAuthorized("Ada");
    }

    [Fact]
    public void Search_and_facets_come_from_the_url_and_go_back_into_it()
    {
        var state = new DataTableState("dri", [new SelectedFacet("category", "Category", FacetOperator.Or, "Tools", "Category eq 'Tools'")], []);
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo($"/gadgets?gadgets={state.Encode()}");

        var table = Render<CoworkeeDataTable<Gadget>>(p => p.Add(t => t.EntitySet, "Gadgets").Add(t => t.SearchFields, ["Name"]).Add(t => t.Columns, Columns()));

        table.WaitForAssertion(() => _queries.Last().Filter.ShouldBe("(Category eq 'Tools') and (contains(tolower(Name),'dri'))"));
        table.Find("[data-testid='facet-chips']").TextContent.ShouldContain("Tools");
        table.Find("input[data-testid='table-search'], [data-testid='table-search'] input").Input("lamp");
        table.WaitForAssertion(() => DataTableState.Decode(Query(nav, "gadgets"))!.Search.ShouldBe("lamp"), TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task Hidden_columns_and_search_are_saved_as_a_view_and_restored()
    {
        var popovers = Render<MudPopoverProvider>();
        Render<MudSnackbarProvider>();
        var table = Render<CoworkeeDataTable<Gadget>>(p => p.Add(t => t.EntitySet, "Gadgets").Add(t => t.SearchFields, ["Name"]).Add(t => t.Columns, Columns()));
        table.WaitForAssertion(() => table.Markup.ShouldContain("Drill"));

        table.Find("input[data-testid='table-search'], [data-testid='table-search'] input").Input("dri");
        await table.Find("[data-testid='columns'] button").ClickAsync(new());
        await MenuItem(popovers, "[data-column='Category']").ClickAsync(new());
        table.WaitForAssertion(() => (table.Instance.CurrentState.Search, table.Instance.CurrentState.HiddenColumns.Single()).ShouldBe(("dri", "Category")), TimeSpan.FromSeconds(3));

        await table.Find("[data-testid='views'] button").ClickAsync(new());
        popovers.WaitForElement("input[data-testid='view-name'], [data-testid='view-name'] input").Input("Drills");
        await popovers.Find("[data-testid='save-view']").ClickAsync(new());

        table.Find("input[data-testid='table-search'], [data-testid='table-search'] input").Input("");
        if (popovers.FindAll("[data-column='Category']").Count == 0)
        {
            await table.Find("[data-testid='columns'] button").ClickAsync(new());
        }
        await MenuItem(popovers, "[data-column='Category']").ClickAsync(new());
        table.WaitForAssertion(() => table.Instance.CurrentState.IsEmpty.ShouldBeTrue(), TimeSpan.FromSeconds(3));

        if (popovers.FindAll("[data-view='Drills']").Count == 0)
        {
            await table.Find("[data-testid='views'] button").ClickAsync(new());
        }

        await MenuItem(popovers, "[data-view='Drills']").ClickAsync(new());

        table.WaitForAssertion(() => (table.Instance.CurrentState.Search, table.Instance.CurrentState.HiddenColumns.Single()).ShouldBe(("dri", "Category")));
    }

    private static AngleSharp.Dom.IElement MenuItem(IRenderedComponent<MudPopoverProvider> popovers, string inner)
    {
        popovers.WaitForElement(inner);
        return popovers.FindAll(".mud-menu-item").First(item => item.QuerySelector(inner) is not null);
    }

    private static string? Query(NavigationManager nav, string key) => System.Web.HttpUtility.ParseQueryString(new Uri(nav.Uri).Query)[key];

    private static RenderFragment Columns() => builder =>
    {
        builder.OpenComponent<PropertyColumn<Gadget, string>>(0);
        builder.AddComponentParameter(1, nameof(PropertyColumn<Gadget, string>.Property), (System.Linq.Expressions.Expression<Func<Gadget, string>>)(g => g.Name));
        builder.CloseComponent();
        builder.OpenComponent<PropertyColumn<Gadget, string>>(2);
        builder.AddComponentParameter(3, nameof(PropertyColumn<Gadget, string>.Property), (System.Linq.Expressions.Expression<Func<Gadget, string>>)(g => g.Category));
        builder.CloseComponent();
    };

    public sealed record Gadget(Guid Id, string Name, string Category);
}
