using Bunit;
using Coworkee.Client.Blazor.Reporting.Pages;
using Coworkee.Client.Blazor.Reporting.State;
using Coworkee.Contracts.Reporting;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Coworkee.Client.Blazor.Reporting.Tests;

/// <summary>Boards close the gaps of widgets a reader does not get.</summary>
public sealed class ReportLayoutTests : ReportingTestBase
{
    private static ReportWidget Tile(string id, int x, int y, int w, int h, string set = "SalesOrders") => new()
    {
        Id = id, Type = WidgetType.Kpi, Title = id, X = x, Y = y, W = w, H = h, Query = new WidgetQuery { EntitySet = set },
    };

    private static ReportWidget[] KpiRow() => [Tile("a", 0, 1, 3, 2), Tile("cost", 3, 1, 3, 2, "InsightCosts"), Tile("c", 6, 1, 3, 2), Tile("d", 9, 1, 3, 2)];

    private static (int X, int Y, int W, int H)[] Positions(IEnumerable<ReportWidget> widgets) => [.. widgets.Select(w => (w.X, w.Y, w.W, w.H))];

    [Fact]
    public void A_hidden_tile_in_a_row_gives_its_width_to_the_others()
    {
        var all = KpiRow();

        var laid = ReportLayout.ForReader(all, [all[0], all[2], all[3]]);

        laid.Select(w => w.Id).ShouldBe(["a", "c", "d"]);
        Positions(laid).ShouldBe([(0, 0, 4, 2), (4, 0, 4, 2), (8, 0, 4, 2)]);
        Positions(all).ShouldBe([(0, 1, 3, 2), (3, 1, 3, 2), (6, 1, 3, 2), (9, 1, 3, 2)]);
    }

    [Fact]
    public void Widths_scale_to_whole_columns_and_the_remainder_goes_to_the_last()
    {
        ReportWidget[] all = [Tile("a", 0, 0, 2, 2), Tile("b", 2, 0, 3, 2), Tile("hidden", 5, 0, 2, 2), Tile("c", 7, 0, 5, 2)];

        var laid = ReportLayout.ForReader(all, [all[0], all[1], all[3]]);

        // span 12 over widths 2/3/5: 2.4 -> 2, 3.6 -> 3, the last takes the rest
        Positions(laid).ShouldBe([(0, 0, 2, 2), (2, 0, 3, 2), (5, 0, 7, 2)]);
    }

    [Fact]
    public void A_hidden_full_row_closes_upward()
    {
        ReportWidget[] all = [Tile("hidden", 0, 0, 12, 2), Tile("next", 0, 2, 6, 4), Tile("side", 6, 2, 6, 3), Tile("below", 6, 5, 6, 1)];

        var laid = ReportLayout.ForReader(all, [all[1], all[2], all[3]]);

        Positions(laid).ShouldBe([(0, 0, 6, 4), (6, 0, 6, 3), (6, 3, 6, 1)]);
    }

    [Fact]
    public void A_row_does_not_widen_over_another_widget()
    {
        // a tall widget stands between the tiles: widening the first would cover it
        ReportWidget[] all = [Tile("a", 0, 0, 3, 2), Tile("tall", 3, 0, 3, 4), Tile("hidden", 6, 0, 3, 2), Tile("tail", 0, 2, 3, 2)];

        var laid = ReportLayout.ForReader(all, [all[0], all[1], all[3]]);

        Positions(laid).ShouldBe([(0, 0, 3, 2), (3, 0, 3, 4), (0, 2, 3, 2)]);
    }

    [Fact]
    public void Nothing_hidden_keeps_the_saved_widgets()
    {
        var all = KpiRow();

        var laid = ReportLayout.ForReader(all, all);

        laid.ShouldBe(all);
    }

    private IRenderedComponent<ReportPage> Open(bool edit, params ReportWidget[] widgets)
    {
        var id = Guid.NewGuid();
        Reports.GetReportAsync(id, default).ReturnsForAnyArgs(new ReportDto(id, "R", null, null, ReportVisibility.Private, [], "Ada", true, false,
            new ReportDefinition { Widgets = [.. widgets] }, DateTimeOffset.UtcNow));
        Reports.QueryAsync(default!, default).ReturnsForAnyArgs(Rows(("North", null, 3)));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"reports/{id}{(edit ? "?edit=true" : string.Empty)}");
        Render<MudBlazor.MudPopoverProvider>();
        var page = Render<ReportPage>(p => p.Add(x => x.Id, id));
        page.WaitForElement("[data-testid='report-page']");
        return page;
    }

    private static (string?, string?, string?, string?, string?)[] GridItems(IRenderedComponent<ReportPage> page) =>
        [.. page.FindAll(".grid-stack-item").Select(e => (e.GetAttribute("data-widget"), e.GetAttribute("gs-x"), e.GetAttribute("gs-y"), e.GetAttribute("gs-w"), e.GetAttribute("gs-h")))];

    [Fact]
    public void A_reader_without_a_source_sees_the_row_without_a_gap()
    {
        var page = Open(false, KpiRow());

        GridItems(page).ShouldBe([("a", "0", "0", "4", "2"), ("c", "4", "0", "4", "2"), ("d", "8", "0", "4", "2")]);
    }

    [Fact]
    public void Layout_stays_as_saved_while_editing()
    {
        var page = Open(true, KpiRow());

        GridItems(page).ShouldBe([("a", "0", "1", "3", "2"), ("cost", "3", "1", "3", "2"), ("c", "6", "1", "3", "2"), ("d", "9", "1", "3", "2")]);
    }
}
