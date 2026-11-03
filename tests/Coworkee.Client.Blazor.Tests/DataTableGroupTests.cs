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

        table.FindAll("[data-testid='group-head']")[1].Click();

        table.WaitForAssertion(() => table.FindAll("[data-testid='group-rows'] tbody tr.mud-table-row").Count.ShouldBe(25));
        _odata.Queries.ShouldContain(q => q.Filter == "(Name ne 'none') and (Kind eq 'Beta')" && q.Top == 25 && q.Skip == 0);
        table.Find("[data-testid='group-rows']").TextContent.ShouldNotContain("Alpha");
    }

    [Fact]
    public void Opening_a_group_of_an_endless_table_scrolls_its_rows()
    {
        var table = Grouped(DataTablePageMode.EndlessScroll, nameof(Row.Kind));
        table.WaitForAssertion(() => Heads(table).Count.ShouldBe(3));

        table.FindAll("[data-testid='group-head']")[2].Click();

        table.WaitForAssertion(() => table.Find("[data-testid='group-rows'] .mud-table-body-virtualized"));
        table.WaitForAssertion(() => _odata.Queries.ShouldContain(q => q.Filter == "(Name ne 'none') and (Kind eq 'Gamma')" && q.Top == 50));
    }

    [Fact]
    public void A_group_of_several_columns_opens_the_groups_of_the_next_column()
    {
        var table = Grouped(DataTablePageMode.Pager, nameof(Row.Kind), nameof(Row.Even));
        table.WaitForAssertion(() => Heads(table).Count.ShouldBe(3));

        table.FindAll("[data-testid='group-head']")[0].Click();

        table.WaitForAssertion(() => Heads(table).ShouldBe(["Kind: Alpha 300", "Even: False 150", "Even: True 150", "Kind: Beta 150", "Kind: Gamma 50"]));
        _odata.Groupings.ShouldContain(("(Name ne 'none') and (Kind eq 'Alpha')", "Even"));
        _odata.Queries.ShouldNotContain(q => q.Top > 0);
    }

    [Fact]
    public void Open_groups_stay_open_when_the_table_reloads()
    {
        var table = Grouped(DataTablePageMode.Pager, nameof(Row.Kind));
        table.WaitForAssertion(() => Heads(table).Count.ShouldBe(3));
        table.FindAll("[data-testid='group-head']")[1].Click();
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

        public Task<ODataPage<T>> QueryAsync<T>(string entitySet, ODataQuery query, CancellationToken cancellationToken = default)
        {
            Queries.Enqueue(query);
            var rows = Matching(query.Filter).ToList();
            IReadOnlyList<T> items = (IReadOnlyList<T>)(object)rows.Skip(query.Skip ?? 0).Take(query.Top ?? 500).ToList();
            return Task.FromResult(new ODataPage<T>(items, query.Count ? rows.Count : null, query.Facets ? Facets : []));
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
