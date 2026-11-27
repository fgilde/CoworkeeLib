using System.Globalization;
using Bunit;
using Coworkee.Client.Blazor.Reporting.Components;
using Coworkee.Client.Blazor.Reporting.State;
using Coworkee.Client.Blazor.Reporting.Widgets;
using Coworkee.Contracts.Reporting;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;

namespace Coworkee.Client.Blazor.Reporting.Tests;

/// <summary>The grouped table widget: keys and values of the main and joined sets, sorted and paged on the server.</summary>
public sealed class GroupedTableTests : ReportingTestBase
{
    private static readonly ReportSourceDto Returns = new("Returns", "Returns",
    [
        new ReportFieldDto("Region", "Region", ReportFieldKind.Text),
        new ReportFieldDto("Status", "Status", ReportFieldKind.Enum, ["Open", "Shipped"], "Shop.Status"),
        new ReportFieldDto("Amount", "Amount", ReportFieldKind.Number),
    ]);

    // columns: Region 0, Status 1, Count 2, Revenue 3, Average Total 4, Returned 5
    private static ReportWidget Lanes(string? orderBy = null, params string[] hidden) => new()
    {
        Id = "lanes", Type = WidgetType.GroupedTable, Title = "Lanes",
        Query = new WidgetQuery
        {
            EntitySet = "SalesOrders", GroupFields = ["Region", "Status"], OrderBy = orderBy,
            Aggregates = [new(), new(AggregateFunction.Sum, "Total", "Revenue"), new(AggregateFunction.Average, "Total")],
            Joins = [new WidgetJoin { EntitySet = "Returns", Aggregates = [new(AggregateFunction.Sum, "Amount", "Returned")] }],
        },
        Options = new WidgetOptions { HiddenColumns = [.. hidden] },
    };

    private static ReportTableRow Row(string? region, string? status, params double?[] values) =>
        new([region is null ? new ReportKey(null, "", "Region eq null") : new ReportKey(region, region, $"Region eq '{region}'"),
            status is null ? new ReportKey(null, "", "Status eq null") : new ReportKey(status, status, $"Status eq Shop.Status'{status}'")], values);

    private static ReportTableResult Page(bool more, params ReportTableRow[] rows) => new(rows, more, 4);

    private static ReportTableResult Page(int count, bool more, int from = 0) =>
        Page(more, [.. Enumerable.Range(from, count).Select(i => Row($"Region {i:00}", "Open", i, i * 10, 1, null))]);

    private ReportState StateOf(ReportWidget widget, params ReportSourceDto[] sources) =>
        new(new ReportDefinition { Widgets = [widget] }, sources.Length == 0 ? [Orders, Returns] : sources);

    private IRenderedComponent<WidgetHost> Show(ReportWidget widget, ReportState? state = null) =>
        Render<WidgetHost>(p => p.Add(x => x.Widget, widget).Add(x => x.State, state ?? StateOf(widget)));

    private static string N(double value) => ReportNumbers.Format(value, new WidgetOptions(), CultureInfo.CurrentCulture);

    [Fact]
    public void A_grouped_table_shows_keys_and_values_of_the_main_and_joined_sources()
    {
        Reports.TableAsync(default!, default).ReturnsForAnyArgs(Page(false, Row("North", "Open", 3, 1200.5, 400.25, null), Row("South", null, 1, 80, 80, 12)));
        var widget = Lanes();
        var state = StateOf(widget);
        // a facet on the main set's region narrows the joined returns as well (same field)
        state.Definition.Filters = [new ReportFilterValue("SalesOrders", "Region", "Region", "North", "Region eq 'North'", Field: "Region")];

        var table = Show(widget, state);

        table.WaitForAssertion(() => table.FindAll("[data-testid='grouped-row']").Count.ShouldBe(2));
        table.FindAll("[data-testid='grouped-table'] th").Select(h => h.TextContent.Trim()).ShouldBe(["Region", "Status", "Count", "Revenue", "Average Total", "Returned"]);
        var first = table.FindAll("[data-testid='grouped-row']")[0].QuerySelectorAll("td");
        first.Select(c => c.TextContent.Trim()).ShouldBe(["North", "Open", "3", N(1200.5), N(400.25), "–"]);
        first.Skip(2).ShouldAllBe(c => c.ClassList.Contains("number"));
        first[0].ClassList.Contains("number").ShouldBeFalse();
        table.FindAll("[data-testid='grouped-row']")[1].QuerySelectorAll("td")[1].TextContent.Trim().ShouldBe("(empty)");
        Reports.Received().TableAsync(Arg.Is<ReportTableRequest>(r => r.EntitySet == "SalesOrders" && r.GroupFields.SequenceEqual(new[] { "Region", "Status" })
            && r.Aggregates.Count == 3 && r.Skip == 0 && r.Top == ReportTableColumns.PageSize && r.Filter == "Region eq 'North'"
            && r.Joins!.Single().EntitySet == "Returns" && r.Joins!.Single().Filter == "Region eq 'North'" && r.Joins!.Single().Aggregates.Single().Label == "Returned"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Clicking_a_header_sorts_on_the_server()
    {
        Reports.TableAsync(default!, default).ReturnsForAnyArgs(Page(false, Row("North", "Open", 3, 1200.5, 400.25, 7)));
        var table = Show(Lanes("Revenue desc"));
        table.WaitForAssertion(() => table.FindAll("[data-testid='grouped-row']").Count.ShouldBe(1));
        Reports.Received().TableAsync(Arg.Is<ReportTableRequest>(r => r.OrderBy!.Single() == new ReportTableSort(3, true)), Arg.Any<CancellationToken>());

        table.Find("th button[data-column='5']").Click();
        table.Find("th button[data-column='5']").Click();

        table.WaitForAssertion(() => Reports.ReceivedCalls().Last().GetArguments()[0].ShouldBeOfType<ReportTableRequest>().OrderBy.ShouldBe([new ReportTableSort(5, true)]));
        ((ReportTableRequest)Reports.ReceivedCalls().Last().GetArguments()[0]!).Skip.ShouldBe(0);
        table.Find("th[aria-sort='descending'] button").GetAttribute("data-column").ShouldBe("5");
    }

    [Fact]
    public void Scrolling_loads_the_next_page()
    {
        Reports.TableAsync(Arg.Is<ReportTableRequest>(r => r.Skip == 0), Arg.Any<CancellationToken>()).Returns(Page(20, more: true));
        Reports.TableAsync(Arg.Is<ReportTableRequest>(r => r.Skip == 20), Arg.Any<CancellationToken>()).Returns(Page(5, more: false, from: 20));
        var table = Show(Lanes());
        table.WaitForAssertion(() => table.FindAll("[data-testid='grouped-row']").Count.ShouldBe(20));
        var widget = table.FindComponent<GroupedTableWidget>();

        // the end of the scroll area came into view (reporting.js calls this)
        var more = table.InvokeAsync(() => widget.Instance.LoadMoreAsync()).GetAwaiter().GetResult();

        more.ShouldBeFalse();
        table.WaitForAssertion(() => table.FindAll("[data-testid='grouped-row']").Count.ShouldBe(25));
        table.FindAll("[data-testid='grouped-row']")[24].TextContent.ShouldContain("Region 24");
        table.InvokeAsync(() => widget.Instance.LoadMoreAsync()).GetAwaiter().GetResult().ShouldBeFalse();
        Reports.Received(2).TableAsync(Arg.Any<ReportTableRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Hidden_columns_are_not_shown_but_still_group()
    {
        Reports.TableAsync(default!, default).ReturnsForAnyArgs(Page(false, Row("North", "Open", 3, 1200.5, 400.25, 7)));

        var table = Show(Lanes(null, "Status", "revenue"));

        table.WaitForAssertion(() => table.FindAll("[data-testid='grouped-row']").Count.ShouldBe(1));
        table.FindAll("[data-testid='grouped-table'] th").Select(h => h.TextContent.Trim()).ShouldBe(["Region", "Count", "Average Total", "Returned"]);
        table.FindAll("[data-testid='grouped-row'] td").Select(c => c.TextContent.Trim()).ShouldBe(["North", "3", N(400.25), "7"]);
        Reports.Received().TableAsync(Arg.Is<ReportTableRequest>(r => r.GroupFields.Count == 2 && r.Aggregates.Count == 3), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Columns_of_unreadable_joins_vanish_and_the_default_order_falls_back()
    {
        Reports.TableAsync(default!, default).ReturnsForAnyArgs(Page(false, Row("North", "Open", 3, 1200.5, 400.25)));
        var widget = Lanes("Returned desc, Count desc");

        // the reader may not read Returns: the server lists only Orders
        var frame = Render<WidgetFrame>(p => p.Add(x => x.Widget, widget).Add(x => x.State, StateOf(widget, Orders)));

        frame.WaitForAssertion(() => frame.FindAll("[data-testid='grouped-row']").Count.ShouldBe(1));
        frame.FindAll("[data-testid='grouped-table'] th").Select(h => h.TextContent.Trim()).ShouldNotContain("Returned");
        frame.FindAll("[data-testid='widget-error']").ShouldBeEmpty();
        Reports.Received().TableAsync(Arg.Is<ReportTableRequest>(r => r.Joins!.Count == 0 && r.OrderBy!.SequenceEqual(new[] { new ReportTableSort(2, true) })), Arg.Any<CancellationToken>());
        widget.Query!.Joins.ShouldHaveSingleItem();
    }

    [Fact]
    public void The_designer_edits_group_fields_and_hidden_columns()
    {
        var widget = Lanes("Revenue desc");
        var changes = 0;
        Render<MudPopoverProvider>();
        var settings = Render<WidgetSettings>(p => p.Add(x => x.Widget, widget).Add(x => x.State, StateOf(widget)).Add(x => x.OnChanged, () => changes++));
        MudSelect<string> Select(string testId, int index = 0) =>
            settings.FindComponents<MudSelect<string>>().Where(s => s.Instance.UserAttributes.GetValueOrDefault("data-testid") as string == testId).ElementAt(index).Instance;

        // group fields: add, change, reorder and remove
        settings.FindAll("[data-testid='group-field']").Count.ShouldBe(2);
        settings.Find("[data-testid='add-group-field']").Click();
        widget.Query!.GroupFields.ShouldBe(["Region", "Status", "OrderDate"]);
        settings.InvokeAsync(() => Select("group-field-path", 2).ValueChanged.InvokeAsync("Customer/Name")).GetAwaiter().GetResult();
        widget.Query.GroupFields.ShouldBe(["Region", "Status", "Customer/Name"]);
        settings.FindAll("[data-testid='group-field'] button[aria-label='Move up']")[2].Click();
        widget.Query.GroupFields.ShouldBe(["Region", "Customer/Name", "Status"]);
        settings.FindAll("[data-testid='group-field'] button[aria-label='Remove']")[0].Click();
        widget.Query.GroupFields.ShouldBe(["Customer/Name", "Status"]);

        // hidden columns: grouped but not shown
        settings.InvokeAsync(() => Select("hidden-columns").SelectedValuesChanged.InvokeAsync(["Status", "Revenue"])).GetAwaiter().GetResult();
        widget.Options.HiddenColumns.ShouldBe(["Status", "Revenue"]);

        // order: a second column, then descending
        settings.Find("[data-testid='add-sort']").Click();
        settings.InvokeAsync(() => Select("sort-column", 1).ValueChanged.InvokeAsync("Count")).GetAwaiter().GetResult();
        settings.FindAll("[data-testid='sort'] [data-testid='sort-direction']")[1].Click();
        widget.Query.OrderBy.ShouldBe("Revenue desc, Count desc");

        // values: a further one
        settings.Find("[data-testid='add-aggregate']").Click();
        widget.Query.Aggregates.Count.ShouldBe(4);

        // joins are shown as chips and can be removed, not added (R21)
        settings.Find("[data-testid='join']").TextContent.ShouldContain("Returns");
        settings.InvokeAsync(() => settings.FindComponents<MudChip<string>>().Single().Instance.OnClose.InvokeAsync(null!)).GetAwaiter().GetResult();
        widget.Query.Joins.ShouldBeEmpty();
        settings.FindAll("[data-testid='join']").ShouldBeEmpty();
        changes.ShouldBeGreaterThan(8);
    }

    [Fact]
    public void A_change_to_a_joined_set_reloads_the_report()
    {
        var id = Guid.NewGuid();
        Reports.GetSourcesAsync(default).ReturnsForAnyArgs([Orders, Returns with { Topic = "type:Return" }]);
        Reports.GetReportAsync(id, default).ReturnsForAnyArgs(new ReportDto(id, "Lanes", null, null, ReportVisibility.Private, [], "Ada", true, false,
            new ReportDefinition { Widgets = [Lanes()] }, DateTimeOffset.UtcNow));
        Reports.TableAsync(default!, default).ReturnsForAnyArgs(Page(false));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"reports/{id}");

        var page = Render<Pages.ReportPage>(p => p.Add(x => x.Id, id));

        page.WaitForAssertion(() => page.FindComponents<Blazor.Components.RealtimeSubscription>().Select(s => s.Instance.Topic).ShouldContain("type:Return"));
    }

    [Fact]
    public void A_new_grouped_table_groups_by_the_first_category()
    {
        var widget = WidgetPresets.Create(WidgetPresets.All.Single(p => p.Type == WidgetType.GroupedTable), [Orders], []);

        widget.Query!.GroupFields.ShouldBe(["Status"]);
        widget.Query.Aggregates.ShouldHaveSingleItem().Function.ShouldBe(AggregateFunction.Count);
    }
}
