using System.Text.Json;
using Bunit;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Coworkee.Client.Blazor.Tests;

public sealed class DataTableTests : ClientTestBase
{
    private static readonly FacetGroupDto Category = new("category", "Category", "Category", FacetKind.CheckboxList, FacetOperator.Or,
        [Option("Tools", 2), Option("Light", 1)], true, 0);

    private readonly FakeOData _odata = new();

    public DataTableTests()
    {
        Services.AddSingleton<IODataClient>(_odata);
        AddAuthorization().SetAuthorized("Ada");
    }

    private static FacetOptionDto Option(string value, int count) =>
        new(JsonSerializer.SerializeToElement(value), value, true, count, $"Category eq '{value}'");

    [Fact]
    public void Queries_combine_paging_sorting_filters_and_count()
    {
        new ODataQuery { Filter = "A eq 'x'", OrderBy = "Name desc", Top = 25, Skip = 50 }.ToQueryString()
            .ShouldBe("?$filter=A%20eq%20%27x%27&$orderby=Name%20desc&$top=25&$skip=50&$count=true");
        new ODataQuery { Expand = "Brand", Count = false }.ToQueryString().ShouldBe("?$expand=Brand");
        ODataFilter.Search("O'Neil", ["Name", "Email"]).ShouldBe("(contains(tolower(Name),'o''neil') or contains(tolower(Email),'o''neil'))");
        ODataFilter.And("A eq 1", null, "(B eq 2 or B eq 3)").ShouldBe("(A eq 1) and (B eq 2 or B eq 3)");
    }

    [Fact]
    public void Facet_options_join_with_or_inside_a_group_and_with_and_between_groups()
    {
        var selection = new FacetSelection();
        var brand = new FacetGroupDto("brand", "Brand", "BrandId", FacetKind.CheckboxList, FacetOperator.Or,
            [new FacetOptionDto(default, "Acme", true, 3, "BrandId eq 1")], true, 1);

        selection.Toggle(Category, Category.Options[0]);
        selection.Toggle(Category, Category.Options[1]);
        selection.Toggle(brand, brand.Options[0]);

        selection.ToFilter().ShouldBe("(Category eq 'Tools' or Category eq 'Light') and BrandId eq 1");
        selection.Toggle(Category, Category.Options[0]);
        selection.ToFilter().ShouldBe("Category eq 'Light' and BrandId eq 1");
    }

    [Fact]
    public void A_burst_of_changes_in_another_session_reloads_the_rows_once()
    {
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        Services.AddSingleton<TimeProvider>(clock);
        var realtime = Services.GetRequiredService<FakeRealtimeConnection>();
        var table = Render<CoworkeeDataTable<Gadget>>(p => p.Add(t => t.EntitySet, "Gadgets").Add(t => t.Columns, Columns()));
        table.WaitForAssertion(() => realtime.Invocations.ShouldContain(("Subscribe", "type:Gadget")));
        table.WaitForAssertion(() => _odata.Loads.ShouldBe(1));

        realtime.Push("type:Gadget", Contracts.Realtime.RealtimeEventTypes.EntityChanged, new Contracts.Realtime.EntityChangedPayload("Gadget", "1", "Deleted", []));
        clock.Advance(TimeSpan.FromMilliseconds(100));
        realtime.Push("type:Gadget", Contracts.Realtime.RealtimeEventTypes.EntityChanged, new Contracts.Realtime.EntityChangedPayload("Gadget", "2", "Deleted", []));
        clock.Advance(TimeSpan.FromMilliseconds(200));
        _odata.Loads.ShouldBe(1, "the burst is still settling");

        // under load a push is handled a little later than the clock moved: step on until the one reload
        table.WaitForAssertion(() =>
        {
            clock.Advance(TimeSpan.FromMilliseconds(100));
            _odata.Loads.ShouldBe(2);
        });
        clock.Advance(TimeSpan.FromSeconds(5));
        table.WaitForAssertion(() => _odata.Loads.ShouldBe(2));
    }

    [Fact]
    public void An_empty_realtime_topic_keeps_the_table_quiet()
    {
        var realtime = Services.GetRequiredService<FakeRealtimeConnection>();
        var table = Render<CoworkeeDataTable<Gadget>>(p => p.Add(t => t.EntitySet, "Gadgets").Add(t => t.RealtimeTopic, "").Add(t => t.Columns, Columns()));
        table.WaitForAssertion(() => _odata.Loads.ShouldBe(1));

        realtime.Invocations.ShouldBeEmpty();
    }

    [Fact]
    public void Registered_toolbar_items_get_the_table()
    {
        Services.AddSingleton(new DataTableToolbarItem(typeof(ToolbarProbe)));

        var table = Render<CoworkeeDataTable<Gadget>>(p => p.Add(t => t.EntitySet, "Gadgets").Add(t => t.Filter, "Category eq 'Tools'").Add(t => t.Columns, Columns()));

        table.WaitForAssertion(() => table.Find("[data-testid='probe']").TextContent.ShouldBe("Gadgets: Category eq 'Tools'"));
    }

    private sealed class ToolbarProbe : ComponentBase
    {
        [Parameter] public IDataTable Table { get; set; } = null!;

        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "span");
            builder.AddAttribute(1, "data-testid", "probe");
            builder.AddContent(2, $"{Table.EntitySet}: {Table.CurrentQuery.Filter}");
            builder.CloseElement();
        }
    }

    [Fact]
    public void A_new_filter_from_the_page_reloads_the_rows()
    {
        var table = Render<CoworkeeDataTable<Gadget>>(p => p.Add(t => t.EntitySet, "Gadgets").Add(t => t.Filter, "Category eq 'Tools'").Add(t => t.Columns, Columns()));
        table.WaitForAssertion(() => _odata.LastQuery!.Filter.ShouldBe("Category eq 'Tools'"));

        table.Render(p => p.Add(t => t.Filter, "Category eq 'Light'"));

        table.WaitForAssertion(() => _odata.LastQuery!.Filter.ShouldBe("Category eq 'Light'"));
    }

    [Fact]
    public void A_refused_query_says_so_and_leaves_no_rows_of_the_last_load()
    {
        var snackbars = Render<MudSnackbarProvider>();
        var table = Render<CoworkeeDataTable<Gadget>>(p => p.Add(t => t.EntitySet, "Gadgets").Add(t => t.Filter, "Category eq 'Tools'").Add(t => t.Columns, Columns()));
        table.WaitForAssertion(() => table.FindAll("tbody tr").Count.ShouldBe(2));

        _odata.Refuse = true;
        table.Render(p => p.Add(t => t.Filter, "Unknown eq null"));

        table.WaitForAssertion(() => snackbars.Markup.ShouldContain("The data could not be loaded."));
        table.WaitForAssertion(() => table.FindAll("tbody tr td").Count.ShouldBeLessThanOrEqualTo(1));
    }

    [Fact]
    public async Task The_table_loads_pages_shows_facets_and_reloads_with_the_chosen_ones()
    {
        var popovers = Render<MudPopoverProvider>();
        var table = Render<CoworkeeDataTable<Gadget>>(p => p
            .Add(t => t.EntitySet, "Gadgets")
            .Add(t => t.SearchFields, ["Name"])
            .Add(t => t.Columns, Columns()));

        table.WaitForAssertion(() => table.Markup.ShouldContain("Drill"));
        (_odata.LastSet, _odata.LastQuery!.Filter).ShouldBe(("Gadgets", null as string));
        table.WaitForAssertion(() => _odata.FacetLoads.ShouldBe(1));
        // the facets render after their request: click the button of that render
        table.WaitForAssertion(() => table.Find("[data-facet='category'] button").Click());
        await popovers.WaitForElement("[data-option=\"Category eq 'Tools'\"]").ClickAsync(new());

        table.WaitForAssertion(() => _odata.LastQuery!.Filter.ShouldBe("Category eq 'Tools'"));
        table.WaitForAssertion(() => table.Find("[data-testid='facet-chips']").TextContent.ShouldContain("Tools"));
        table.Find("input[data-testid='table-search'], [data-testid='table-search'] input").Input("dri");
        table.WaitForAssertion(() => _odata.LastQuery!.Filter.ShouldBe("(Category eq 'Tools') and (contains(tolower(Name),'dri'))"), TimeSpan.FromSeconds(3));
    }

    // the set's declared search fields (AddODataEntity SearchFields, AddGlobalSearch) when the page names none; a local copy searches while typing
    [Theory]
    [InlineData(false, 300)]
    [InlineData(true, 90)]
    public void Without_own_search_fields_the_table_searches_those_the_server_declares(bool local, int debounce)
    {
        var search = new FakeGlobalSearchApi();
        search.SearchFields["Gadgets"] = ["Name", "Brand/Name"];
        Services.AddSingleton<Search.IGlobalSearchApi>(search);
        Services.AddScoped<Search.SearchFieldsCatalog>();
        Services.AddSingleton<IClientDataCache>(new LocalCache(local ? "Gadgets" : null));
        var table = Render<CoworkeeDataTable<Gadget>>(p => p.Add(t => t.EntitySet, "Gadgets").Add(t => t.Columns, Columns()));
        table.WaitForAssertion(() => table.Markup.ShouldContain("Drill"));

        table.FindComponent<MudTextField<string>>().Instance.DebounceInterval.ShouldBe(debounce);
        table.Find("input[data-testid='table-search'], [data-testid='table-search'] input").Input("dri");

        table.WaitForAssertion(() => _odata.LastQuery!.Filter.ShouldBe("(contains(tolower(Name),'dri') or contains(tolower(Brand/Name),'dri'))"), TimeSpan.FromSeconds(3));
        Render<CoworkeeDataTable<Gadget>>(p => p.Add(t => t.EntitySet, "Gadgets").Add(t => t.Searchable, false).Add(t => t.Columns, Columns()))
            .FindAll("[data-testid='table-search']").ShouldBeEmpty();
    }

    private sealed class LocalCache(string? local) : IClientDataCache
    {
        public Task ClearAsync() => Task.CompletedTask;

        public bool AnswersLocally(string entitySet) => entitySet == local;
    }

    [Fact]
    public async Task Create_edit_and_delete_run_the_page_callbacks_and_reload()
    {
        var created = 0;
        Gadget? edited = null;
        IReadOnlyCollection<Gadget>? deleted = null;
        var dialogs = Render<MudDialogProvider>();
        Render<MudPopoverProvider>();
        var table = Render<CoworkeeDataTable<Gadget>>(p => p
            .Add(t => t.EntitySet, "Gadgets")
            .Add(t => t.Columns, Columns())
            .Add(t => t.MultiSelection, true)
            .Add(t => t.OnCreate, () => created++)
            .Add(t => t.OnEdit, (Gadget g) => edited = g)
            .Add(t => t.OnDelete, items => { deleted = items; return Task.CompletedTask; }));
        table.WaitForAssertion(() => table.FindAll("[data-testid='edit-row']").Count.ShouldBe(2));
        var loads = _odata.Loads;

        await table.Find("[data-testid='create']").ClickAsync(new());
        await table.FindAll("[data-testid='edit-row']")[1].ClickAsync(new());
        var deleting = table.FindAll("[data-testid='delete-row']")[0].ClickAsync(new());
        dialogs.WaitForAssertion(() => dialogs.FindAll("button").Any(b => b.TextContent.Trim() == "Delete").ShouldBeTrue());
        await dialogs.FindAll("button").First(b => b.TextContent.Trim() == "Delete").ClickAsync(new());
        await deleting;

        created.ShouldBe(1);
        edited!.Name.ShouldBe("Lamp");
        table.WaitForAssertion(() => deleted!.Single().Name.ShouldBe("Drill"));
        _odata.Loads.ShouldBeGreaterThan(loads);
    }

    [Fact]
    public async Task Excel_export_sends_the_current_filter_and_columns_and_import_opens_the_wizard()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        NSubstitute.SubstituteExtensions.ReturnsForAnyArgs(Transfer.StreamExportAsync(default!, default!, default), AsyncEnumerable.Empty<DataExportProgress>());
        NSubstitute.SubstituteExtensions.ReturnsForAnyArgs(Transfer.GetTargetAsync(default!, default),
            Task.FromResult(new ImportTargetDto("Gadgets", [new ImportFieldDto("Name", "Name", "String", true)], false, false, [])));
        var dialogs = Render<MudDialogProvider>();
        var popovers = Render<MudPopoverProvider>();
        Render<MudSnackbarProvider>();
        var table = Render<CoworkeeDataTable<Gadget>>(p => p
            .Add(t => t.EntitySet, "Gadgets")
            .Add(t => t.Filter, "Price gt 1")
            .Add(t => t.Importable, true)
            .Add(t => t.ExportFileName, "gadgets")
            .Add(t => t.Columns, Columns("Gadget, name")));
        table.WaitForAssertion(() => table.Markup.ShouldContain("Drill"));

        await table.Find("[data-testid='export'] button").ClickAsync(new());
        await popovers.WaitForElement("[data-testid='export-excel']").ClickAsync(new());
        table.WaitForAssertion(() => NSubstitute.Received.InOrder(() => Transfer.StreamExportAsync("Gadgets",
            ExportQueries.Of(ExportFileFormat.Xlsx, new ODataQuery { Filter = "Price gt 1" }, [("Name", "Gadget, name")], "gadgets"), NSubstitute.Arg.Any<CancellationToken>())));
        dialogs.WaitForAssertion(() => dialogs.FindAll("[data-testid='export-progress']").ShouldBeEmpty());

        _ = table.Find("[data-testid='import']").ClickAsync(new()); // the wizard stays open, so the click does not finish
        dialogs.WaitForAssertion(() => dialogs.Markup.ShouldContain("Choose a CSV, Excel or JSON file"));
    }

    [Fact]
    public void The_export_query_escapes_the_headers_and_names_the_file()
    {
        var query = System.Web.HttpUtility.ParseQueryString(ExportQueries.Of(ExportFileFormat.Csv, new ODataQuery(), [("Name", "Gadget, name: full"), ("Category", null)], "my gadgets"));

        ExportColumns.Parse(query["columns"]).ShouldBe([("Name", "Gadget, name: full"), ("Category", null)]);
        query["fileName"].ShouldBe("my gadgets");
    }

    [Fact]
    public async Task A_data_source_replaces_the_odata_set_and_excel_stays_with_odata()
    {
        var source = NSubstitute.Substitute.For<ITableDataSource<Gadget>>();
        NSubstitute.SubstituteExtensions.ReturnsForAnyArgs(source.QueryAsync(default!, default),
            Task.FromResult(new ODataPage<Gadget>([new Gadget(Guid.Empty, "Kettle", "Kitchen")], 1, [])));
        var popovers = Render<MudPopoverProvider>();
        var table = Render<CoworkeeDataTable<Gadget>>(p => p.Add(t => t.EntitySet, "Local").Add(t => t.DataSource, source).Add(t => t.Columns, Columns()));

        table.WaitForAssertion(() => table.Markup.ShouldContain("Kettle"));
        _odata.Loads.ShouldBe(0);
        await table.Find("[data-testid='export'] button").ClickAsync(new());
        popovers.WaitForElements(".mud-menu-item").Select(i => i.TextContent.Trim()).ShouldBe(["CSV", "JSON"]);
    }

    [Fact]
    public void Columns_in_markup_take_the_item_type_from_the_table()
    {
        var table = Render<GadgetTable>();

        table.WaitForAssertion(() => table.Markup.ShouldContain("Tools"));
    }

    [Fact]
    public void Csv_export_quotes_what_needs_quotes()
    {
        var csv = TableExport.ToCsv([new Gadget(Guid.Empty, "Drill; heavy", "Tools")]);

        csv.Split(Environment.NewLine)[0].ShouldBe("Id;Name;Category");
        csv.ShouldContain("\"Drill; heavy\"");
        TableExport.ToCsv([new Gadget(Guid.Empty, "=HYPERLINK(1)", "-x")]).ShouldContain("'=HYPERLINK(1);'-x");
    }

    private static RenderFragment Columns(string? title = null) => builder =>
    {
        builder.OpenComponent<PropertyColumn<Gadget, string>>(0);
        builder.AddComponentParameter(1, nameof(PropertyColumn<Gadget, string>.Property), (System.Linq.Expressions.Expression<Func<Gadget, string>>)(g => g.Name));
        if (title is not null)
        {
            builder.AddComponentParameter(2, nameof(PropertyColumn<Gadget, string>.Title), title);
        }

        builder.CloseComponent();
    };

    public sealed record Gadget(Guid Id, string Name, string Category);

    [Fact]
    public async Task Facets_come_in_a_request_of_their_own_once_per_filter_and_the_rows_do_not_wait()
    {
        var odata = new GatedOData();
        Services.AddSingleton<IODataClient>(odata);
        var table = Render<CoworkeeDataTable<Gadget>>(p => p.Add(t => t.EntitySet, "Gadgets").Add(t => t.SearchFields, ["Name"]).Add(t => t.Columns, Columns()));

        // the rows show while the facets are still on their way
        table.WaitForAssertion(() => table.Markup.ShouldContain("Drill"));
        table.FindAll("[data-facet='category']").ShouldBeEmpty();
        odata.Queries.Where(q => q.Top != 0).ShouldAllBe(q => !q.Facets && q.FacetContext == null);
        odata.Queries.Single(q => q.Facets).ShouldSatisfyAllConditions(q => q.Top.ShouldBe(0), q => q.Count.ShouldBeFalse(), q => q.Expand.ShouldBeNull(), q => q.OrderBy.ShouldBeNull());
        odata.Facets.SetResult();
        table.WaitForAssertion(() => table.Find("[data-facet='category']"));

        // another sort or page: rows only
        var grid = table.FindComponent<MudDataGrid<Gadget>>().Instance;
        await table.InvokeAsync(() => grid.SetSortAsync("Name", MudBlazor.SortDirection.Descending, g => g.Name));
        await table.InvokeAsync(() => grid.NavigateTo(Page.Next));
        table.WaitForAssertion(() => odata.Queries.ShouldContain(q => q.OrderBy == "Name desc" && q.Skip == 25));
        odata.Queries.Count(q => q.Facets).ShouldBe(1);

        // another filter: the facets again
        table.Find("input[data-testid='table-search'], [data-testid='table-search'] input").Input("dri");
        table.WaitForAssertion(() => odata.Queries.Count(q => q.Facets).ShouldBe(2), TimeSpan.FromSeconds(3));
        odata.Queries.Last(q => q.Facets).Filter.ShouldBe("contains(tolower(Name),'dri')");
    }

    [Fact]
    public async Task The_pager_shows_the_rows_of_the_next_page_and_back()
    {
        var rows = Enumerable.Range(1, 60).Select(i => new Gadget(Guid.CreateVersion7(), $"Item {i:00}", "Tools")).ToArray();
        Services.AddSingleton<IODataClient>(new PagingODataClient<Gadget>(rows, Category));
        var table = Render<CoworkeeDataTable<Gadget>>(p => p.Add(t => t.EntitySet, "Gadgets").Add(t => t.Columns, Columns()));
        table.WaitForAssertion(() => table.Markup.ShouldContain("Item 01"));

        // first, previous, next, last
        await table.InvokeAsync(() => table.FindAll(".mud-table-pagination-actions button")[2].ClickAsync(new()));

        table.WaitForAssertion(() => table.Markup.ShouldContain("Item 26"));
        table.Markup.ShouldNotContain("Item 01");
        // the facets came in between: the following pages render their rows too
        table.WaitForAssertion(() => table.Find("[data-facet='category']"));
        await table.InvokeAsync(() => table.FindAll(".mud-table-pagination-actions button")[2].ClickAsync(new()));
        table.WaitForAssertion(() => table.Markup.ShouldContain("Item 51"));
        table.Markup.ShouldNotContain("Item 26");
        await table.InvokeAsync(() => table.FindAll(".mud-table-pagination-actions button")[1].ClickAsync(new()));
        table.WaitForAssertion(() => table.Markup.ShouldContain("Item 26"));
        table.Markup.ShouldNotContain("Item 51");
    }

    [Fact]
    public async Task The_pager_keeps_loading_pages_when_the_first_load_was_cancelled()
    {
        var rows = Enumerable.Range(1, 100).Select(i => new Gadget(Guid.CreateVersion7(), $"Item {i:00}", "Tools")).ToArray();
        var odata = new PagingODataClient<Gadget>(rows) { HoldNextRows = new TaskCompletionSource() };
        Services.AddSingleton<IODataClient>(odata);
        var table = Render<CoworkeeDataTable<Gadget>>(p => p.Add(t => t.EntitySet, "Gadgets").Add(t => t.Columns, Columns()));
        table.WaitForAssertion(() => odata.Queries.Count(q => q.Top > 0).ShouldBe(1));

        // a reload while the grid's first load is on its way cancels that load
        await table.InvokeAsync(table.Instance.ReloadAsync);
        table.WaitForAssertion(() => table.Markup.ShouldContain("Item 01"));

        foreach (var first in new[] { "Item 26", "Item 51", "Item 76" })
        {
            await table.InvokeAsync(() => table.FindAll(".mud-table-pagination-actions button")[2].ClickAsync(new()));
            table.WaitForAssertion(() => table.Markup.ShouldContain(first));
        }
    }

    [Fact]
    public async Task Another_page_starts_at_the_first_row()
    {
        var module = JSInterop.SetupModule("./_content/Coworkee.Client.Blazor/coworkee.js");
        module.SetupVoid("scrollRowsToTop", _ => true);
        var odata = new GatedOData();
        odata.Facets.SetResult();
        Services.AddSingleton<IODataClient>(odata);
        var table = Render<CoworkeeDataTable<Gadget>>(p => p.Add(t => t.EntitySet, "Gadgets").Add(t => t.Columns, Columns()));
        table.WaitForAssertion(() => table.Markup.ShouldContain("Drill"));
        module.Invocations.Count(i => i.Identifier == "scrollRowsToTop").ShouldBe(0, "the first page is at its top already");

        var grid = table.FindComponent<MudDataGrid<Gadget>>().Instance;
        await table.InvokeAsync(() => grid.NavigateTo(Page.Next));

        table.WaitForAssertion(() => module.Invocations.Count(i => i.Identifier == "scrollRowsToTop").ShouldBe(1));
    }

    [Fact]
    public void Facets_counted_on_a_sample_say_how_many_rows_they_counted()
    {
        var odata = new FakeODataClient().With("Gadgets", new Gadget(Guid.CreateVersion7(), "Drill", "Tools"))
            .WithFacets(Category with { SampleRows = 20_000, TotalRows = 210_000 });
        Services.AddSingleton<IODataClient>(odata);
        var table = Render<CoworkeeDataTable<Gadget>>(p => p.Add(t => t.EntitySet, "Gadgets").Add(t => t.Columns, Columns()));

        table.WaitForAssertion(() => table.Find("[data-testid='facets-sampled']").TextContent.ShouldContain("20,000"));
        table.Find("[data-testid='facets-sampled']").TextContent.ShouldContain("210,000");
    }

    [Fact]
    public void Facets_of_every_row_show_no_sample_hint()
    {
        Services.AddSingleton<IODataClient>(new FakeODataClient().With("Gadgets", new Gadget(Guid.CreateVersion7(), "Drill", "Tools")).WithFacets(Category));
        var table = Render<CoworkeeDataTable<Gadget>>(p => p.Add(t => t.EntitySet, "Gadgets").Add(t => t.Columns, Columns()));

        table.WaitForAssertion(() => table.Find("[data-facet='category']"));
        table.FindAll("[data-testid='facets-sampled']").ShouldBeEmpty();
    }

    [Fact]
    public void A_source_that_answers_at_once_counts_the_facets_after_the_rows()
    {
        // a local copy answers synchronously: counting its facets first would keep the last page on the screen until done
        var odata = new GatedOData();
        odata.Facets.SetResult();
        Services.AddSingleton<IODataClient>(odata);
        var table = Render<CoworkeeDataTable<Gadget>>(p => p.Add(t => t.EntitySet, "Gadgets").Add(t => t.Columns, Columns()));

        table.WaitForAssertion(() => table.Find("[data-facet='category']"));
        var queries = odata.Queries.ToList();
        queries.FindIndex(q => !q.Facets).ShouldBeLessThan(queries.FindIndex(q => q.Facets));
    }

    private sealed class FakeOData : IODataClient
    {
        public string? LastSet { get; private set; }

        /// <summary>The last query for rows; facets come in queries of their own.</summary>
        public ODataQuery? LastQuery { get; private set; }

        public int Loads { get; private set; }

        public int FacetLoads { get; private set; }

        /// <summary>The server refuses every query, as it does a filter on a property it does not know.</summary>
        public bool Refuse { get; set; }

        public Task<ODataPage<T>> QueryAsync<T>(string entitySet, ODataQuery query, CancellationToken cancellationToken = default)
        {
            if (Refuse)
            {
                throw new Api.ApiException(400, "odata.failed", null);
            }

            LastSet = entitySet;
            if (query.Top == 0)
            {
                FacetLoads++;
            }
            else
            {
                (LastQuery, Loads) = (query, Loads + 1);
            }

            IReadOnlyList<T> items = (IReadOnlyList<T>)(object)new List<Gadget> { new(Guid.CreateVersion7(), "Drill", "Tools"), new(Guid.CreateVersion7(), "Lamp", "Light") };
            return Task.FromResult(new ODataPage<T>(items, 2, query.Facets ? [Category] : []));
        }
    }

    // facets wait until the test lets them through; 100 rows, so there is a second page
    private sealed class GatedOData : IODataClient
    {
        public TaskCompletionSource Facets { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public System.Collections.Concurrent.ConcurrentQueue<ODataQuery> Queries { get; } = new();

        public async Task<ODataPage<T>> QueryAsync<T>(string entitySet, ODataQuery query, CancellationToken cancellationToken = default)
        {
            Queries.Enqueue(query);
            if (query.Facets)
            {
                await Facets.Task.WaitAsync(cancellationToken);
            }

            IReadOnlyList<T> items = (IReadOnlyList<T>)(object)new List<Gadget> { new(Guid.CreateVersion7(), "Drill", "Tools"), new(Guid.CreateVersion7(), "Lamp", "Light") };
            return new ODataPage<T>(items, 100, query.Facets ? [Category] : []);
        }
    }
}
