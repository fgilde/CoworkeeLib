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
        // the rows' queries; the facets come in requests of their own, after the rows
        _odata.Queried += (_, query) => { if (!query.Facets) { _queries.Add(query); } };
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
        await SetColumnVisibleAsync(table, popovers, "Category", false);
        table.WaitForAssertion(() => (table.Instance.CurrentState.Search, table.Instance.CurrentState.HiddenColumns.Single()).ShouldBe(("dri", "Category")), TimeSpan.FromSeconds(3));

        await OpenAsync(table, popovers, "views", "[data-testid='save-view']");
        popovers.Find("input[data-testid='view-name'], [data-testid='view-name'] input").Input("Drills");
        await ClickAsync(popovers, "[data-testid='save-view']");

        table.Find("input[data-testid='table-search'], [data-testid='table-search'] input").Input("");
        await SetColumnVisibleAsync(table, popovers, "Category", true);
        table.WaitForAssertion(() => table.Instance.CurrentState.IsEmpty.ShouldBeTrue(), TimeSpan.FromSeconds(3));

        await OpenAsync(table, popovers, "views", "[data-view='Drills']");
        await ClickAsync(popovers, "[data-view='Drills']");

        table.WaitForAssertion(() => (table.Instance.CurrentState.Search, table.Instance.CurrentState.HiddenColumns.Single()).ShouldBe(("dri", "Category")));
    }

    internal static async Task OpenAsync<TTable>(IRenderedComponent<TTable> table, IRenderedComponent<MudPopoverProvider> popovers, string menu, string inner)
        where TTable : Microsoft.AspNetCore.Components.IComponent
    {
        if (popovers.FindAll(inner).Count == 0)
        {
            // found and clicked on the renderer's thread, so no render comes in between
            await table.InvokeAsync(() => table.Find($"[data-testid='{menu}'] button").ClickAsync(new()));
        }

        popovers.WaitForElement(inner);
    }

    // the popover may render once more between finding and clicking (the views load asynchronously): bUnit then knows the old handler id
    // no more; find it again and click once more
    private static async Task ClickAsync(IRenderedComponent<MudPopoverProvider> popovers, string selector)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await popovers.InvokeAsync(() => popovers.Find(selector).ClickAsync(new()));
                return;
            }
            catch (Bunit.Rendering.UnknownEventHandlerIdException) when (attempt < 3)
            {
                popovers.WaitForElement(selector);
            }
        }
    }

    internal static async Task SetColumnVisibleAsync<TTable>(IRenderedComponent<TTable> table, IRenderedComponent<MudPopoverProvider> popovers, string column, bool visible)
        where TTable : Microsoft.AspNetCore.Components.IComponent
    {
        await OpenAsync(table, popovers, "columns", $"[data-column='{column}']");
        await popovers.Find($"[data-column='{column}'] input").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = visible });
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
