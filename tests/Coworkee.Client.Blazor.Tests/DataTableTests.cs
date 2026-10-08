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
    public async Task The_table_loads_pages_shows_facets_and_reloads_with_the_chosen_ones()
    {
        var popovers = Render<MudPopoverProvider>();
        var table = Render<CoworkeeDataTable<Gadget>>(p => p
            .Add(t => t.EntitySet, "Gadgets")
            .Add(t => t.SearchFields, ["Name"])
            .Add(t => t.Columns, Columns()));

        table.WaitForAssertion(() => table.Markup.ShouldContain("Drill"));
        (_odata.LastSet, _odata.LastQuery!.Filter, _odata.LastQuery.Facets).ShouldBe(("Gadgets", null as string, true));
        await table.Find("[data-facet='category'] button").ClickAsync(new());
        await popovers.WaitForElement("[data-option=\"Category eq 'Tools'\"]").ClickAsync(new());

        table.WaitForAssertion(() => _odata.LastQuery!.Filter.ShouldBe("Category eq 'Tools'"));
        table.WaitForAssertion(() => table.Find("[data-testid='facet-chips']").TextContent.ShouldContain("Tools"));
        table.Find("input[data-testid='table-search'], [data-testid='table-search'] input").Input("dri");
        table.WaitForAssertion(() => _odata.LastQuery!.Filter.ShouldBe("(Category eq 'Tools') and (contains(tolower(Name),'dri'))"), TimeSpan.FromSeconds(3));
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
    public async Task Excel_export_sends_the_current_filter_and_import_reports_failed_rows()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var dialogs = Render<MudDialogProvider>();
        var popovers = Render<MudPopoverProvider>();
        Render<MudSnackbarProvider>();
        var table = Render<CoworkeeDataTable<Gadget>>(p => p
            .Add(t => t.EntitySet, "Gadgets")
            .Add(t => t.Filter, "Price gt 1")
            .Add(t => t.Importable, true)
            .Add(t => t.Columns, Columns()));
        table.WaitForAssertion(() => table.Markup.ShouldContain("Drill"));

        await table.Find("[data-testid='export'] button").ClickAsync(new());
        await popovers.WaitForElement(".mud-menu-item").ClickAsync(new());
        table.WaitForAssertion(() => _odata.Exported!.Filter.ShouldBe("Price gt 1"));

        table.FindComponent<Microsoft.AspNetCore.Components.Forms.InputFile>().UploadFiles(InputFileContent.CreateFromBinary([1], "gadgets.xlsx"));
        table.WaitForAssertion(() => _odata.Imported.ShouldBe("Gadgets/gadgets.xlsx"));
        dialogs.WaitForAssertion(() => dialogs.Markup.ShouldContain("Row 3: Name: required"));
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

    private static RenderFragment Columns() => builder =>
    {
        builder.OpenComponent<PropertyColumn<Gadget, string>>(0);
        builder.AddComponentParameter(1, nameof(PropertyColumn<Gadget, string>.Property), (System.Linq.Expressions.Expression<Func<Gadget, string>>)(g => g.Name));
        builder.CloseComponent();
    };

    public sealed record Gadget(Guid Id, string Name, string Category);

    private sealed class FakeOData : IODataClient
    {
        public string? LastSet { get; private set; }

        public ODataQuery? LastQuery { get; private set; }

        public int Loads { get; private set; }

        public Task<ODataPage<T>> QueryAsync<T>(string entitySet, ODataQuery query, CancellationToken cancellationToken = default)
        {
            (LastSet, LastQuery) = (entitySet, query);
            Loads++;
            IReadOnlyList<T> items = (IReadOnlyList<T>)(object)new List<Gadget> { new(Guid.CreateVersion7(), "Drill", "Tools"), new(Guid.CreateVersion7(), "Lamp", "Light") };
            return Task.FromResult(new ODataPage<T>(items, 2, [Category]));
        }

        public ODataQuery? Exported { get; private set; }

        public string? Imported { get; private set; }

        public Task<byte[]> ExportAsync(string entitySet, ODataQuery query, CancellationToken cancellationToken = default)
        {
            Exported = query;
            return Task.FromResult<byte[]>([1, 2]);
        }

        public Task<ImportResult> ImportAsync(string entitySet, Stream workbook, string fileName, CancellationToken cancellationToken = default)
        {
            Imported = $"{entitySet}/{fileName}";
            return Task.FromResult(new ImportResult(1, [new ImportRowError(3, "Name: required")]));
        }
    }
}
