using Microsoft.Extensions.DependencyInjection;
using Bunit;
using Coworkee.Client.Blazor.Reporting.Components;
using Coworkee.Client.Blazor.Reporting.Pages;
using Coworkee.Contracts.Reporting;
using Microsoft.AspNetCore.Components;
using NSubstitute;

namespace Coworkee.Client.Blazor.Reporting.Tests;

/// <summary>The designer against a fake GridStack module: it adds, moves and removes widgets through the grid's JS interop.</summary>
public sealed class DesignerTests : ReportingTestBase
{
    private static readonly Guid Id = Guid.NewGuid();
    private IRenderedComponent<MudBlazor.MudPopoverProvider> _popovers = null!;

    private ReportDto Report(params ReportWidget[] widgets) =>
        new(Id, "Sales", "Revenue", null, ReportVisibility.Private, [], "Ada", true, false, new ReportDefinition { Widgets = [.. widgets] }, DateTimeOffset.UtcNow);

    private IRenderedComponent<ReportPage> Open(ReportDto report, bool edit = true)
    {
        Reports.GetReportAsync(Id, default).ReturnsForAnyArgs(report);
        Reports.QueryAsync(default!, default).ReturnsForAnyArgs(Rows(("North", null, 3)));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"reports/{Id}{(edit ? "?edit=true" : string.Empty)}");
        _popovers = Render<MudBlazor.MudPopoverProvider>();
        var page = Render<ReportPage>(p => p.Add(x => x.Id, Id));
        page.WaitForElement("[data-testid='report-page']");
        return page;
    }

    [Fact]
    public void The_grid_starts_GridStack_and_follows_the_edit_mode()
    {
        var page = Open(Report(new ReportWidget { Id = "k", Type = WidgetType.Markdown, Options = new() { Text = "Hi" } }), edit: false);

        var init = Grid.VerifyInvoke("init");
        init.Arguments[2].ShouldBe(false);
        init.Arguments[3].ShouldBe(ReportDefinition.GridColumns);
        page.Find("[data-testid='report-edit']").Click();

        page.WaitForAssertion(() => Grid.VerifyInvoke("setEditable").Arguments[1].ShouldBe(true));
        page.Find("[data-testid='report-save']");
    }

    [Fact]
    public void Adding_from_the_palette_places_a_widget_below_and_opens_its_settings()
    {
        var page = Open(Report(new ReportWidget { Id = "k", Type = WidgetType.Markdown, Y = 0, H = 3, Options = new() { Text = "Hi" } }));

        page.Find("[data-testid='add-widget']").Click();
        _popovers.WaitForElement("[data-preset='Bar chart']").Click();

        page.WaitForAssertion(() => page.FindAll(".grid-stack-item").Count.ShouldBe(2));
        var added = page.FindAll(".grid-stack-item")[1];
        added.GetAttribute("gs-y").ShouldBe("3");
        added.GetAttribute("gs-w").ShouldBe("6");
        page.Find("[data-testid='widget-settings-panel']");
        page.Find("[data-testid='widget-source']");
        Grid.Invocations.Count(i => i.Identifier == "sync").ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Moves_reported_by_GridStack_update_the_layout_and_can_be_undone()
    {
        var page = Open(Report(new ReportWidget { Id = "a", Type = WidgetType.Markdown, X = 0, Y = 0, W = 4, H = 3, Options = new() { Text = "A" } }));
        var grid = page.FindComponent<ReportGrid>();

        await page.InvokeAsync(() => grid.Instance.OnLayoutChanged([new ReportGrid.GridItem("a", 6, 2, 6, 4)]));

        page.WaitForAssertion(() => page.Find(".grid-stack-item").GetAttribute("gs-x").ShouldBe("6"));
        page.Find(".grid-stack-item").GetAttribute("gs-h").ShouldBe("4");
        page.Find("[data-testid='undo']").Click();
        page.WaitForAssertion(() => page.Find(".grid-stack-item").GetAttribute("gs-x").ShouldBe("0"));
        page.Find("[data-testid='redo']").HasAttribute("disabled").ShouldBeFalse();
    }

    [Fact]
    public void Removing_a_widget_takes_it_off_the_grid_and_saving_sends_the_new_layout()
    {
        var page = Open(Report(
            new ReportWidget { Id = "a", Type = WidgetType.Markdown, Options = new() { Text = "A" } },
            new ReportWidget { Id = "b", Type = WidgetType.Markdown, Y = 3, Options = new() { Text = "B" } }));
        Reports.UpdateAsync(Id, default!, default).ReturnsForAnyArgs(c => Report([.. c.Arg<SaveReportRequest>().Definition.Widgets]));

        page.FindAll("[data-testid='widget-menu'] button")[0].Click();
        _popovers.WaitForElement("[data-testid='widget-remove']").Click();
        page.WaitForAssertion(() => page.FindAll(".grid-stack-item").Count.ShouldBe(1));
        page.Find("[data-testid='report-save']").Click();

        page.WaitForAssertion(() => Reports.Received().UpdateAsync(Id, Arg.Is<SaveReportRequest>(r => r.Definition.Widgets.Single().Id == "b"), Arg.Any<CancellationToken>()));
        page.WaitForElement("[data-testid='report-edit']");
    }
}
