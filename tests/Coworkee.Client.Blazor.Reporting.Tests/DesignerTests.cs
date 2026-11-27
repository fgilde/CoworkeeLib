using Microsoft.Extensions.DependencyInjection;
using Bunit;
using Coworkee.Client.Blazor.Reporting.Components;
using Coworkee.Client.Blazor.Reporting.Pages;
using Coworkee.Contracts.Reporting;
using Microsoft.AspNetCore.Components;
using Coworkee.Client.Blazor.Reporting.State;
using MudBlazor;
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

    private static readonly ReportSourceDto Trips = new("Trips", "Trips",
    [
        new ReportFieldDto("Eta", "ETA", ReportFieldKind.Date),
        new ReportFieldDto("Etd", "ETD", ReportFieldKind.Date),
        new ReportFieldDto("Carrier", "Carrier", ReportFieldKind.Text),
        new ReportFieldDto("Weight", "Weight", ReportFieldKind.Number),
    ]);

    private static readonly ReportWidget TripChart = new() { Id = "t", Type = WidgetType.Chart, Query = new WidgetQuery { EntitySet = "Trips", DateField = "Eta", GroupBy = new ReportGroupBy("Eta", DateBucket.Month) } };

    private static MudSelect<string> Select(IRenderedComponent<MudDialogProvider> dialogs, string testId) =>
        dialogs.FindComponents<MudSelect<string>>().Single(s => s.Instance.UserAttributes.GetValueOrDefault("data-testid") as string == testId).Instance;

    [Fact]
    public async Task The_report_settings_choose_the_date_fields_and_the_filter_bar_fields_and_the_viewer_follows()
    {
        Reports.GetSourcesAsync(default).ReturnsForAnyArgs([Trips]);
        var report = Report(TripChart);
        Reports.UpdateAsync(default, default!, default).ReturnsForAnyArgs(report);
        var dialogs = Render<MudDialogProvider>();
        var page = Open(report);
        page.FindAll("[data-testid='date-field']").ShouldBeEmpty();
        page.Find("[data-testid='report-settings-open']").Click();
        dialogs.WaitForElement("[data-testid='report-settings']");

        // only date fields of the widgets' sets are offered for the date choice, any non-key field for the filter bar
        var dates = Select(dialogs, "report-date-fields");
        await dialogs.InvokeAsync(() => dates.SelectedValuesChanged.InvokeAsync(new[] { "Etd", "Eta" }));
        await dialogs.InvokeAsync(() => Select(dialogs, "report-facet-fields").SelectedValuesChanged.InvokeAsync(new[] { "Carrier", "Eta", "Weight" }));
        dialogs.Find("[data-testid='report-settings-ok']").Click();

        page.WaitForElement("[data-testid='date-field']");
        page.Find("[data-testid='report-save']").Click();
        page.WaitForAssertion(() => Reports.Received().UpdateAsync(Id,
            Arg.Is<SaveReportRequest>(r => r.Definition.DateFields.SequenceEqual(new[] { "Etd", "Eta" }) && r.Definition.FacetFields.SequenceEqual(new[] { "Carrier", "Eta", "Weight" })), Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void The_report_settings_offer_only_date_fields_for_the_date_choice_and_every_plain_field_for_the_filter_bar()
    {
        ReportFieldChoices.Dates([Trips, Orders]).Select(f => f.Path).ShouldBe(["Eta", "Etd", "OrderDate"]);
        ReportFieldChoices.Facets([Trips]).Select(f => f.Path).ShouldBe(["Eta", "Etd", "Carrier", "Weight"]);
    }

    [Fact]
    public void Only_rows_with_a_date_is_a_switch_of_charts_and_kpis_with_a_date_field()
    {
        var chart = new ReportWidget { Id = "c", Type = WidgetType.Chart, Query = new WidgetQuery { EntitySet = "Trips", DateField = "Eta" } };
        var changed = 0;
        var state = new ReportState(new ReportDefinition { Widgets = [chart] }, [Trips]);
        var settings = Render<WidgetSettings>(p => p.Add(x => x.Widget, chart).Add(x => x.State, state).Add(x => x.OnChanged, () => changed++));

        var toggle = settings.FindComponents<MudSwitch<bool>>().Single(s => s.Instance.UserAttributes.GetValueOrDefault("data-testid") as string == "widget-require-date").Instance;
        settings.InvokeAsync(() => toggle.ValueChanged.InvokeAsync(true)).GetAwaiter().GetResult();

        chart.Query!.RequireDate.ShouldBeTrue();
        changed.ShouldBe(1);
        chart.Query.DateField = null;
        var without = Render<WidgetSettings>(p => p.Add(x => x.Widget, chart).Add(x => x.State, state));
        without.FindComponents<MudSwitch<bool>>().Any(s => s.Instance.UserAttributes.GetValueOrDefault("data-testid") as string == "widget-require-date").ShouldBeFalse();
    }

    [Fact]
    public void Average_offers_empty_counts_as_zero_and_other_functions_do_not()
    {
        var kpi = new ReportWidget { Id = "k", Type = WidgetType.Kpi, Query = new WidgetQuery { EntitySet = "Trips", Aggregates = [new(AggregateFunction.Average, "Weight")] } };
        var state = new ReportState(new ReportDefinition { Widgets = [kpi] }, [Trips]);
        var settings = Render<WidgetSettings>(p => p.Add(x => x.Widget, kpi).Add(x => x.State, state));
        MudSwitch<bool> Toggle() => settings.FindComponents<MudSwitch<bool>>().Single(s => s.Instance.UserAttributes.GetValueOrDefault("data-testid") as string == "aggregate-empty-as-zero").Instance;

        settings.InvokeAsync(() => Toggle().ValueChanged.InvokeAsync(true)).GetAwaiter().GetResult();

        kpi.Query!.Aggregates[0].EmptyAsZero.ShouldBeTrue();
        kpi.Query.Aggregates[0] = new ReportAggregate(AggregateFunction.Sum, "Weight");
        var sum = Render<WidgetSettings>(p => p.Add(x => x.Widget, kpi).Add(x => x.State, state));
        sum.FindComponents<MudSwitch<bool>>().Any(s => s.Instance.UserAttributes.GetValueOrDefault("data-testid") as string == "aggregate-empty-as-zero").ShouldBeFalse();
    }

    [Fact]
    public void Chips_are_a_chart_kind_with_an_editor_for_the_icons_of_their_values()
    {
        var widget = new ReportWidget { Type = WidgetType.Chart, Query = new WidgetQuery { EntitySet = "SalesOrders", GroupBy = new ReportGroupBy("Region") }, Options = new() { Chart = ChartKind.Bar } };
        var changes = 0;
        var settings = Render<WidgetSettings>(p => p.Add(x => x.Widget, widget).Add(x => x.State, State(widget)).Add(x => x.OnChanged, () => changes++));
        settings.FindAll("[data-testid='value-icon']").ShouldBeEmpty();

        settings.Find("[data-chart='Chips']").Click();

        widget.Options.Chart.ShouldBe(ChartKind.Chips);
        settings.Find("[data-testid='add-value-icon']").Click();
        settings.Find("[data-testid='add-value-icon']").HasAttribute("disabled").ShouldBeTrue();
        settings.FindAll("[data-testid='value-icon'] input")[0].Change("Air");
        settings.FindAll("[data-testid='value-icon']")[0].QuerySelector("[data-icon='Flight']")!.Click();
        settings.Find("[data-testid='add-value-icon']").Click();
        settings.FindAll("[data-testid='value-icon']").Count.ShouldBe(2);
        settings.FindAll("[data-testid='value-icon'] input")[1].Change("Sea");
        settings.FindAll("[data-testid='value-icon']")[1].QuerySelector("[data-icon='DirectionsBoat']")!.Click();

        widget.Options.ValueIcons.ShouldBe(new Dictionary<string, string> { ["Air"] = Icons.Material.Outlined.Flight, ["Sea"] = Icons.Material.Outlined.DirectionsBoat });

        settings.FindAll("[data-testid='value-icon'] button[aria-label='Move down']")[0].Click();

        widget.Options.ValueIcons.Keys.ShouldBe(["Sea", "Air"]);
        settings.FindAll("[data-testid='value-icon'] button[aria-label='Remove']")[0].Click();
        widget.Options.ValueIcons.Keys.ShouldBe(["Air"]);
        changes.ShouldBeGreaterThan(5);
    }
}
