using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Bunit;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Coworkee.Client.Blazor.Tests;

/// <summary>Grouped tables list the groups of the whole result with their counts and load a group's rows when it opens.</summary>
public sealed partial class DataTableGroupTests : ClientTestBase
{
    private readonly Rows _odata = new();

    public DataTableGroupTests()
    {
        Services.AddSingleton<IODataClient>(_odata);
        AddAuthorization().SetAuthorized("Ada");
    }

    [Theory]
    [InlineData(DataTablePageMode.Pager)]
    [InlineData(DataTablePageMode.EndlessScroll)]
    public void Every_group_of_the_result_is_listed_with_its_count_and_no_rows_load_until_it_opens(DataTablePageMode mode)
    {
        var table = Grouped(mode, nameof(Row.Kind));

        table.WaitForAssertion(() => Heads(table).ShouldBe(["Kind: Alpha 300", "Kind: Beta 150", "Kind: Gamma 50"]));
        _odata.Groupings.ShouldBe([("Name ne 'none'", "Kind")]);
        _odata.Queries.ShouldNotContain(q => q.Top > 0, "closed groups load no rows");
        table.Find("[data-testid='table-total']").TextContent.ShouldContain("500");
        table.FindAll("[data-testid='grouped-pages']").ShouldBeEmpty();
    }

    [Fact]
    public void Groups_name_their_values_like_the_facet_of_the_column()
    {
        _odata.Facets =
        [
            new FacetGroupDto("Kind", "Kind", "Kind", FacetKind.CheckboxList, FacetOperator.Or,
                [new FacetOptionDto(System.Text.Json.JsonSerializer.SerializeToElement("Beta"), "Zweite", true, 150, "Kind eq 'Beta'")], true, 0),
        ];

        var table = Grouped(DataTablePageMode.Pager, nameof(Row.Kind));

        table.WaitForAssertion(() => Heads(table).ShouldBe(["Kind: Alpha 300", "Kind: Zweite 150", "Kind: Gamma 50"]));
    }

    [Fact]
    public void Opening_a_group_loads_its_rows_in_pages()
    {
        var table = Grouped(DataTablePageMode.Pager, nameof(Row.Kind));
        table.WaitForAssertion(() => Heads(table).Count.ShouldBe(3));

        ClickHead(table, 1);

        table.WaitForAssertion(() => table.FindAll("[data-testid='group-rows'] tbody tr.mud-table-row").Count.ShouldBe(25));
        _odata.Queries.ShouldContain(q => q.Filter == "(Name ne 'none') and (Kind eq 'Beta')" && q.Top == 25 && q.Skip == 0);
        table.Find("[data-testid='group-rows']").TextContent.ShouldNotContain("Alpha");
    }

    [Fact]
    public void The_pager_of_a_group_sits_left()
    {
        var table = Grouped(DataTablePageMode.Pager, nameof(Row.Kind));
        table.WaitForAssertion(() => Heads(table).Count.ShouldBe(3));

        ClickHead(table, 1);

        // the stylesheet hides the spacer that pushes MudBlazor's pager to the right
        table.WaitForAssertion(() => table.Find("[data-testid='group-rows'] .cw-group-pager .mud-table-pagination-spacer"));
        table.Markup.ShouldContain(".cw-group-pager .mud-table-pagination-spacer { display: none; }");
    }

    [Fact]
    public void Opening_a_group_of_an_endless_table_scrolls_its_rows()
    {
        var table = Grouped(DataTablePageMode.EndlessScroll, nameof(Row.Kind));
        table.WaitForAssertion(() => Heads(table).Count.ShouldBe(3));

        ClickHead(table, 2);

        table.WaitForAssertion(() => table.Find("[data-testid='group-rows'] .mud-table-body-virtualized"));
        table.WaitForAssertion(() => _odata.Queries.ShouldContain(q => q.Filter == "(Name ne 'none') and (Kind eq 'Gamma')" && q.Top == 50));
    }

    [Fact]
    public void A_group_of_several_columns_opens_the_groups_of_the_next_column()
    {
        var table = Grouped(DataTablePageMode.Pager, nameof(Row.Kind), nameof(Row.Even));
        table.WaitForAssertion(() => Heads(table).Count.ShouldBe(3));

        ClickHead(table, 0);

        table.WaitForAssertion(() => Heads(table).ShouldBe(["Kind: Alpha 300", "Even: False 150", "Even: True 150", "Kind: Beta 150", "Kind: Gamma 50"]));
        _odata.Groupings.ShouldContain(("(Name ne 'none') and (Kind eq 'Alpha')", "Even"));
        _odata.Queries.ShouldNotContain(q => q.Top > 0);
    }

    [Fact]
    public void An_opening_group_shows_that_it_loads_until_its_rows_arrive()
    {
        var table = Grouped(DataTablePageMode.Pager, nameof(Row.Kind));
        table.WaitForAssertion(() => Heads(table).Count.ShouldBe(3));
        var gate = _odata.Hold();

        ClickHead(table, 1);

        table.WaitForAssertion(() => table.Find("[data-testid='group-rows-loading']"));
        gate.SetResult();
        table.WaitForAssertion(() => table.FindAll("[data-testid='group-rows'] tbody tr.mud-table-row").Count.ShouldBe(25));
        table.FindAll("[data-testid='group-rows-loading']").ShouldBeEmpty();
    }

    [Theory]
    [InlineData(DataTablePageMode.Pager)]
    [InlineData(DataTablePageMode.EndlessScroll)]
    public void A_group_opened_again_shows_the_rows_it_had_at_once_and_refreshes_them(DataTablePageMode mode)
    {
        var table = Grouped(mode, nameof(Row.Kind));
        table.WaitForAssertion(() => Heads(table).Count.ShouldBe(3));
        ClickHead(table, 2);
        table.WaitForAssertion(() => table.FindAll("[data-testid='group-rows'] tbody tr.mud-table-row").Count.ShouldBeGreaterThan(0));
        ClickHead(table, 2);
        table.WaitForAssertion(() => table.FindAll("[data-testid='group-rows']").ShouldBeEmpty());
        var gate = _odata.Hold();
        var before = _odata.Queries.Count(q => q.Filter?.Contains("Gamma", StringComparison.Ordinal) == true);

        ClickHead(table, 2);

        table.WaitForAssertion(() => table.FindAll("[data-testid='group-rows'] tbody tr.mud-table-row").Count.ShouldBeGreaterThan(0), TimeSpan.FromSeconds(2));
        table.Find("[data-testid='group-rows']").TextContent.ShouldContain("Gamma");
        gate.SetResult();
        table.WaitForAssertion(() => _odata.Queries.Count(q => q.Filter?.Contains("Gamma", StringComparison.Ordinal) == true).ShouldBeGreaterThan(before));
    }

    [Fact]
    public void A_group_opened_again_after_another_filter_shows_no_rows_of_the_filter_before()
    {
        var table = Grouped(DataTablePageMode.Pager, nameof(Row.Kind));
        table.WaitForAssertion(() => Heads(table).Count.ShouldBe(3));
        ClickHead(table, 2);
        table.WaitForAssertion(() => table.Find("[data-testid='group-rows']").TextContent.ShouldContain("Row 451"));
        ClickHead(table, 2);
        table.WaitForAssertion(() => table.FindAll("[data-testid='group-rows']").ShouldBeEmpty());

        table.Render(p => p.Add(t => t.Filter, "Name ne 'none' and Even eq true"));
        table.WaitForAssertion(() => Heads(table).ShouldContain("Kind: Gamma 25"));
        var gate = _odata.Hold();
        // the heads render again while the groups reload
        table.WaitForAssertion(() => table.FindAll("[data-testid='group-head']").Single(h => h.TextContent.Contains("Gamma")).Click());

        // the rows of the filter before (odd ones) do not show while the group loads
        table.WaitForAssertion(() => table.Find("[data-testid='group-rows-loading']"));
        table.Markup.ShouldNotContain("Row 451");
        gate.SetResult();
        table.WaitForAssertion(() => table.Find("[data-testid='group-rows']").TextContent.ShouldContain("Row 450"));
        table.Markup.ShouldNotContain("Row 451");
    }

    [Fact]
    public void Open_groups_stay_open_when_the_table_reloads()
    {
        var table = Grouped(DataTablePageMode.Pager, nameof(Row.Kind));
        table.WaitForAssertion(() => Heads(table).Count.ShouldBe(3));
        ClickHead(table, 1);
        table.WaitForAssertion(() => table.Find("[data-testid='group-rows']"));
        var groupings = _odata.Groupings.Count;

        table.InvokeAsync(table.Instance.ReloadAsync);

        table.WaitForAssertion(() => _odata.Groupings.Count.ShouldBe(groupings + 1));
        table.WaitForAssertion(() => table.FindAll("[data-testid='group-rows']").ShouldHaveSingleItem());
    }

    private IRenderedComponent<CoworkeeDataTable<Row>> Grouped(DataTablePageMode mode, params string[] by)
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo(nav.GetUriWithQueryParameter("rows", (DataTableState.Empty with { GroupBy = [.. by] }).Encode()));
        return Render<CoworkeeDataTable<Row>>(p => p.Add(t => t.EntitySet, "Rows").Add(t => t.PageMode, mode).Add(t => t.Filter, "Name ne 'none'")
            .Add(t => t.Columns, Columns()));
    }

    // the facets arrive after the rows and render the heads again: a click on the head found before would miss its handler
    private static void ClickHead(IRenderedComponent<CoworkeeDataTable<Row>> table, int index) =>
        table.WaitForAssertion(() => table.FindAll("[data-testid='group-head']")[index].Click());

    private static List<string> Heads(IRenderedComponent<CoworkeeDataTable<Row>> table) =>
        [.. table.FindAll("[data-testid='group-head']").Select(h => Regex.Replace(h.TextContent, @"\s+", " ").Trim())];

    private static RenderFragment Columns() => builder =>
    {
        builder.OpenComponent<PropertyColumn<Row, string>>(0);
        builder.AddComponentParameter(1, nameof(PropertyColumn<Row, string>.Property), (System.Linq.Expressions.Expression<Func<Row, string>>)(r => r.Name));
        builder.CloseComponent();
        builder.OpenComponent<PropertyColumn<Row, Kind>>(2);
        builder.AddComponentParameter(3, nameof(PropertyColumn<Row, Kind>.Property), (System.Linq.Expressions.Expression<Func<Row, Kind>>)(r => r.Kind));
        builder.CloseComponent();
        builder.OpenComponent<PropertyColumn<Row, bool>>(4);
        builder.AddComponentParameter(5, nameof(PropertyColumn<Row, bool>.Property), (System.Linq.Expressions.Expression<Func<Row, bool>>)(r => r.Even));
        builder.CloseComponent();
    };

    public enum Kind
    {
        Alpha,
        Beta,
        Gamma,
    }

    public sealed record Row(Guid Id, string Name, Kind Kind, bool Even);

    private sealed partial class Rows : IODataClient
    {
        // 300 Alpha, 150 Beta, 50 Gamma; every second row even
        private readonly Row[] _rows = [.. Enumerable.Range(0, 500).Select(i => new Row(Guid.CreateVersion7(), $"Row {i:000}", i < 300 ? Kind.Alpha : i < 450 ? Kind.Beta : Kind.Gamma, i % 2 == 0))];

        public ConcurrentQueue<ODataQuery> Queries { get; } = new();

        public List<(string? Filter, string By)> Groupings { get; } = [];

        public IReadOnlyList<FacetGroupDto> Facets { get; set; } = [];

        private Task _gate = Task.CompletedTask;

        /// <summary>Holds the row queries back (not the counts) until the returned source completes.</summary>
        public TaskCompletionSource Hold()
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _gate = gate.Task;
            return gate;
        }

        public async Task<ODataPage<T>> QueryAsync<T>(string entitySet, ODataQuery query, CancellationToken cancellationToken = default)
        {
            Queries.Enqueue(query);
            if (query.Top > 0)
            {
                await _gate;
            }

            var rows = Matching(query.Filter).ToList();
            IReadOnlyList<T> items = (IReadOnlyList<T>)(object)rows.Skip(query.Skip ?? 0).Take(query.Top ?? 500).ToList();
            return new ODataPage<T>(items, query.Count ? rows.Count : null, query.Facets ? Facets : []);
        }

        public Task<IReadOnlyList<ODataGroup>> GroupAsync<T>(string entitySet, string? filter, IReadOnlyList<string> by, CancellationToken cancellationToken = default)
        {
            Groupings.Add((filter, string.Join(",", by)));
            var get = PropertyPath.Getter<Row>(by.Single());
            // the server answers in no particular order
            IReadOnlyList<ODataGroup> groups = [.. Matching(filter).GroupBy(get).Select(g => new ODataGroup([g.Key], g.Count())).Reverse()];
            return Task.FromResult(groups);
        }

        private IEnumerable<Row> Matching(string? filter) => _rows.Where(r =>
            (filter is null || !KindIs().Match(filter).Success || r.Kind.ToString() == KindIs().Match(filter).Groups[1].Value)
            && (filter is null || !EvenIs().Match(filter).Success || r.Even.ToString().Equals(EvenIs().Match(filter).Groups[1].Value, StringComparison.OrdinalIgnoreCase)));

        [GeneratedRegex(@"Kind eq '(\w+)'")]
        private static partial Regex KindIs();

        [GeneratedRegex(@"Even eq (\w+)")]
        private static partial Regex EvenIs();
    }
}
