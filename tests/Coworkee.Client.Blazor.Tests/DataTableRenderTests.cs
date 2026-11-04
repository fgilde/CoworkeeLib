using System.Collections.Concurrent;
using System.Text.Json;
using Bunit;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Coworkee.Client.Blazor.Tests;

/// <summary>Rendering the rows is the expensive part of a table in the browser: the table renders them only when they can have changed.</summary>
public sealed class DataTableRenderTests : ClientTestBase
{
    private static readonly FacetGroupDto Category = new("category", "Category", "Category", FacetKind.CheckboxList, FacetOperator.Or,
        [Option("Tools"), Option("Light")], true, 0);

    private readonly GatedOData _odata = new();

    // every render of the rows asks the cell template once per row
    private readonly ConcurrentQueue<string> _cells = new();

    public DataTableRenderTests()
    {
        Services.AddSingleton<IODataClient>(_odata);
        AddAuthorization().SetAuthorized("Ada");
    }

    private static FacetOptionDto Option(string value) => new(JsonSerializer.SerializeToElement(value), value, true, 1, $"Category eq '{value}'");

    [Fact]
    public void Facets_arriving_after_the_rows_do_not_render_the_rows_again()
    {
        var table = Render<CoworkeeDataTable<Gadget>>(p => p.Add(t => t.EntitySet, "Gadgets").Add(t => t.Columns, Columns()));
        table.WaitForAssertion(() => _cells.ShouldContain("Drill"));
        _cells.Clear();

        _odata.Facets.SetResult();

        table.WaitForAssertion(() => table.Find("[data-facet='category']"));
        _cells.ShouldBeEmpty();
    }

    [Fact]
    public void A_facet_choice_renders_the_new_rows_without_another_render_for_the_facets()
    {
        _odata.Facets.SetResult();
        var table = Render<CoworkeeDataTable<Gadget>>(p => p.Add(t => t.EntitySet, "Gadgets").Add(t => t.Columns, Columns()));
        table.WaitForAssertion(() => table.Find("[data-facet='category']"));
        // another sort loads the rows only
        var grid = table.FindComponent<MudDataGrid<Gadget>>().Instance;
        var rowsAlone = RowRenders(table, () => grid.SetSortAsync("Name", MudBlazor.SortDirection.Descending, g => g.Name));

        var withFacets = RowRenders(table, () =>
        {
            table.Instance.Selection.Toggle(Category, Category.Options[0]);
            return Task.CompletedTask;
        });

        table.WaitForAssertion(() => table.Find("[data-testid='facet-chips']"));
        withFacets.ShouldBe(rowsAlone, "the facets of the new filter come without rendering the rows");
    }

    [Fact]
    public void A_column_marked_by_a_chosen_facet_renders_its_filter_icon()
    {
        _odata.Facets.SetResult();
        var table = Render<CoworkeeDataTable<Gadget>>(p => p.Add(t => t.EntitySet, "Gadgets").Add(t => t.Columns, Columns(nameof(Gadget.Category))));
        table.WaitForAssertion(() => table.Find("[data-facet='category']"));
        table.FindAll(".filter-button.filtered").ShouldBeEmpty();

        table.InvokeAsync(() => table.Instance.Selection.Toggle(Category, Category.Options[0]));

        table.WaitForAssertion(() => table.Find(".filter-button.filtered"));
    }

    [Fact]
    public void A_fresh_query_while_scrolling_shows_the_total_without_rendering_the_rows_for_it()
    {
        _odata.Facets.SetResult();
        var table = Render<CoworkeeDataTable<Gadget>>(p => p.Add(t => t.EntitySet, "Gadgets").Add(t => t.PageMode, DataTablePageMode.EndlessScroll).Add(t => t.Columns, Columns()));
        table.WaitForAssertion(() => table.Find("[data-testid='table-total']").TextContent.ShouldContain("2"));
        _cells.Clear();

        table.InvokeAsync(table.Instance.ReloadAsync);

        table.WaitForAssertion(() => _cells.ShouldContain("Drill"));
        Thread.Sleep(200);
        // the grid's own renders once its rows came (MudBlazor renders again for its drop zones)
        _cells.Count(c => c == "Drill").ShouldBe(2);
        table.Find("[data-testid='table-total']").TextContent.ShouldContain("2");
    }

    // the renders of the rows one reload takes, once the table is quiet again
    private int RowRenders(IRenderedComponent<CoworkeeDataTable<Gadget>> table, Func<Task> action)
    {
        var loads = _odata.Queries.Count;
        _cells.Clear();
        table.InvokeAsync(action);
        table.WaitForAssertion(() => _odata.Queries.Count.ShouldBeGreaterThan(loads));
        table.WaitForAssertion(() => _cells.ShouldContain("Drill"));
        Thread.Sleep(200);
        return _cells.Count(c => c == "Drill");
    }

    private RenderFragment Columns(string? property = null) => builder =>
    {
        builder.OpenComponent<TemplateColumn<Gadget>>(0);
        builder.AddComponentParameter(1, nameof(TemplateColumn<Gadget>.CellTemplate), (RenderFragment<CellContext<Gadget>>)(cell => b =>
        {
            _cells.Enqueue(cell.Item.Name);
            b.AddContent(0, cell.Item.Name);
        }));
        builder.CloseComponent();
        if (property is not null)
        {
            builder.OpenComponent<PropertyColumn<Gadget, string>>(2);
            builder.AddComponentParameter(3, nameof(PropertyColumn<Gadget, string>.Property), (System.Linq.Expressions.Expression<Func<Gadget, string>>)(g => g.Category));
            builder.CloseComponent();
        }
    };

    public sealed record Gadget(string Name, string Category);

    // facets wait until the test lets them through
    private sealed class GatedOData : IODataClient
    {
        public TaskCompletionSource Facets { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ConcurrentQueue<ODataQuery> Queries { get; } = new();

        public async Task<ODataPage<T>> QueryAsync<T>(string entitySet, ODataQuery query, CancellationToken cancellationToken = default)
        {
            Queries.Enqueue(query);
            if (query.Facets)
            {
                await Facets.Task.WaitAsync(cancellationToken);
            }

            IReadOnlyList<T> items = (IReadOnlyList<T>)(object)new List<Gadget> { new("Drill", "Tools"), new("Lamp", "Light") };
            return new ODataPage<T>(items, 2, query.Facets ? [Category] : []);
        }
    }
}
