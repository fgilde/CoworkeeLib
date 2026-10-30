using System.Collections.Concurrent;
using Bunit;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;
using Coworkee.Contracts.Settings;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class DataTablePageModeTests : ClientTestBase
{
    private readonly Rows _odata = new();

    public DataTablePageModeTests()
    {
        Services.AddSingleton<IODataClient>(_odata);
        AddAuthorization().SetAuthorized("Ada");
    }

    [Fact]
    public void Endless_scrolling_loads_chunks_without_a_pager_and_shows_the_total()
    {
        var table = Render<CoworkeeDataTable<Row>>(p => p.Add(t => t.EntitySet, "Rows").Add(t => t.PageMode, DataTablePageMode.EndlessScroll).Add(t => t.Columns, Columns()));

        table.WaitForAssertion(() => table.Find("[data-testid='table-total']").TextContent.ShouldContain("500"));
        table.FindAll(".mud-table-pagination-actions").ShouldBeEmpty();
        _odata.Queries.ShouldContain(q => q.Skip == 0 && q.Top == 50 && q.Count && !q.Facets);
        _odata.Queries.ShouldContain(q => q.Top == 0 && q.Facets && !q.Count, "the facets come in a request of their own");
        _odata.Queries.Where(q => q.Skip == 0).Count(q => q.Count).ShouldBe(1, "a page is loaded once per query");
        table.Find(".mud-table-body-virtualized");
    }

    [Fact]
    public void A_changed_page_size_loads_chunks_of_the_new_size()
    {
        var table = Render<CoworkeeDataTable<Row>>(p => p.Add(t => t.EntitySet, "Rows").Add(t => t.PageMode, DataTablePageMode.EndlessScroll).Add(t => t.PageSize, 100).Add(t => t.Columns, Columns()));
        table.WaitForAssertion(() => _odata.Queries.ShouldContain(q => q.Top == 100));

        table.Render(p => p.Add(t => t.PageSize, 200));
        table.InvokeAsync(table.Instance.ReloadAsync);

        table.WaitForAssertion(() => _odata.Queries.ShouldContain(q => q.Skip == 0 && q.Top == 200 && q.Count));
    }

    [Fact]
    public void The_pager_is_the_default_and_the_setting_switches_to_endless_scrolling()
    {
        var table = Render<CoworkeeDataTable<Row>>(p => p.Add(t => t.EntitySet, "Rows").Add(t => t.Columns, Columns()));
        table.WaitForAssertion(() => table.Find(".mud-table-pagination-actions"));
        table.FindAll("[data-testid='table-total']").ShouldBeEmpty();

        Api.GetClientSettingsAsync(default).ReturnsForAnyArgs(new Dictionary<string, string?> { [TableSettings.DefaultPageMode] = TableSettings.EndlessScroll });
        table.InvokeAsync(() => Services.GetRequiredService<Theming.ThemeService>().LoadAsync());

        table.WaitForAssertion(() => table.Find("[data-testid='table-total']").TextContent.ShouldContain("500"));
        table.FindAll(".mud-table-pagination-actions").ShouldBeEmpty();
    }

    [Fact]
    public void Grouped_rows_go_back_to_pages_with_a_hint()
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo(nav.GetUriWithQueryParameter("rows", (DataTableState.Empty with { GroupBy = [nameof(Row.Name)] }).Encode()));

        var table = Render<CoworkeeDataTable<Row>>(p => p.Add(t => t.EntitySet, "Rows").Add(t => t.PageMode, DataTablePageMode.EndlessScroll).Add(t => t.Columns, Columns()));

        table.WaitForAssertion(() => table.Find("[data-testid='grouped-pages']"));
        table.WaitForAssertion(() => table.FindAll("[data-group-key]").ShouldNotBeEmpty());
        table.WaitForAssertion(() => table.Find(".mud-table-pagination-actions"));
    }

    private static RenderFragment Columns() => builder =>
    {
        builder.OpenComponent<PropertyColumn<Row, string>>(0);
        builder.AddComponentParameter(1, nameof(PropertyColumn<Row, string>.Property), (System.Linq.Expressions.Expression<Func<Row, string>>)(r => r.Name));
        builder.CloseComponent();
    };

    public sealed record Row(Guid Id, string Name);

    private sealed class Rows : IODataClient
    {
        private readonly Row[] _rows = [.. Enumerable.Range(0, 500).Select(i => new Row(Guid.CreateVersion7(), $"Row {i:000}"))];

        public ConcurrentQueue<ODataQuery> Queries { get; } = new();

        public Task<ODataPage<T>> QueryAsync<T>(string entitySet, ODataQuery query, CancellationToken cancellationToken = default)
        {
            Queries.Enqueue(query);
            IReadOnlyList<T> items = (IReadOnlyList<T>)(object)_rows.Skip(query.Skip ?? 0).Take(query.Top ?? 500).ToList();
            return Task.FromResult(new ODataPage<T>(items, query.Count ? _rows.Length : null, []));
        }
    }
}
