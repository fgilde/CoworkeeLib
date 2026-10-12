using System.Text.Json;
using AngleSharp.Dom;
using Bunit;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;

namespace Coworkee.Client.Blazor.Tests;

public sealed class FacetViewTests : ClientTestBase
{
    // Sunday, 11 October 2026, noon in UTC+2
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.FromHours(2));

    private readonly FacetSelection _selection = new() { Now = () => Now };

    private static FacetGroupDto Status => Group("status", "Status", FacetValueKind.Enum,
        Option("Status eq 'Open'", "Open", 3), Option("Status eq 'Shipped'", "On the way", 2), Option("Status eq 'Cancelled'", "Cancelled", 0) with { Enabled = false })
        with { AllowExclude = true };

    private static FacetGroupDto Regions => Group("region", "Region", FacetValueKind.Text,
        [.. Enumerable.Range(1, 12).Select(i => Option($"Region eq 'R{i:00}'", $"R{i:00}", 13 - i))]) with { AllowExclude = true };

    [Fact]
    public void The_list_shows_counts_searches_shows_more_and_combines_with_or()
    {
        var view = Render<FacetCheckboxView>(p => p.Add(v => v.Group, Regions).Add(v => v.Selection, _selection));

        view.FindAll("[data-option]").Count.ShouldBe(8);
        view.Find("[data-option=\"Region eq 'R01'\"] .cw-facet-count").TextContent.ShouldBe(12.ToString("N0"));
        view.Find("[data-testid='facet-more']").Click();
        view.FindAll("[data-option]").Count.ShouldBe(12);
        view.Find("[data-option=\"Region eq 'R01'\"]").Click();
        view.Find("[data-option=\"Region eq 'R12'\"]").Click();
        _selection.ToFilter().ShouldBe("(Region eq 'R01' or Region eq 'R12')");

        view.Find("[data-testid='facet-search'] input, input[data-testid='facet-search']").Input("R1");
        view.FindAll("[data-option]").Select(o => o.TextContent.Trim()[..3]).ShouldBe(["R10", "R11", "R12"]);
    }

    [Fact]
    public void Excluding_negates_a_value_and_single_choice_keeps_one()
    {
        var view = Render<FacetCheckboxView>(p => p.Add(v => v.Group, Regions).Add(v => v.Selection, _selection));

        view.FindAll("[data-testid='facet-exclude']")[1].Click();
        view.Find("[data-option=\"Region eq 'R02'\"]").ClassList.ShouldContain("excluded");
        _selection.ToFilter().ShouldBe("not (Region eq 'R02')");

        var single = Regions with { Key = "single", Combine = FacetCombine.Single, AllowExclude = false };
        var radios = Render<FacetCheckboxView>(p => p.Add(v => v.Group, single).Add(v => v.Selection, _selection));
        radios.Find("[data-option=\"Region eq 'R01'\"]").Click();
        radios.Find("[data-option=\"Region eq 'R03'\"]").Click();
        _selection.GroupFilter("single").ShouldBe("Region eq 'R03'");
        radios.FindAll("[data-testid='facet-exclude']").ShouldBeEmpty();
    }

    [Fact]
    public void Chips_show_enum_labels_toggle_and_exclude_with_a_right_click()
    {
        var view = Render<FacetChipView>(p => p.Add(v => v.Group, Status).Add(v => v.Selection, _selection));

        view.Find("[data-option=\"Status eq 'Shipped'\"]").TextContent.ShouldContain("On the way");
        view.Find("[data-option=\"Status eq 'Cancelled'\"]").HasAttribute("disabled").ShouldBeTrue();
        view.Find("[data-option=\"Status eq 'Open'\"]").Click();
        view.Find("[data-option=\"Status eq 'Shipped'\"]").ContextMenu();

        _selection.ToFilter().ShouldBe("(Status eq 'Open' and not (Status eq 'Shipped'))");
        view.Find("[data-option=\"Status eq 'Open'\"]").ClassList.ShouldContain("selected");
    }

    [Fact]
    public void The_dropdown_chooses_several_values()
    {
        var popovers = Render<MudPopoverProvider>();
        var view = Render<FacetSelectView>(p => p.Add(v => v.Group, Status).Add(v => v.Selection, _selection));

        view.Find(".mud-input-control").MouseDown();
        popovers.WaitForElement("[data-option=\"Status eq 'Open'\"]").Click();
        popovers.WaitForElement("[data-option=\"Status eq 'Shipped'\"]").Click();

        view.WaitForAssertion(() => _selection.ToFilter().ShouldBe("(Status eq 'Open' or Status eq 'Shipped')"));
    }

    [Fact]
    public void Buttons_of_a_single_choice_offer_all_and_one_value()
    {
        var single = Status with { Combine = FacetCombine.Single };
        var view = Render<FacetButtonView>(p => p.Add(v => v.Group, single).Add(v => v.Selection, _selection));

        view.Find("[data-option='']").ClassList.ShouldContain("selected");
        view.Find("[data-option=\"Status eq 'Open'\"]").Click();
        view.Find("[data-option=\"Status eq 'Shipped'\"]").Click();
        _selection.ToFilter().ShouldBe("Status eq 'Shipped'");
        view.Find("[data-option='']").Click();
        _selection.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Swatches_take_the_color_of_the_option()
    {
        var colored = Status with { Options = [Status.Options[0] with { Meta = new Dictionary<string, string> { ["color"] = "#00838f" } }, Status.Options[1]] };
        var view = Render<FacetSwatchView>(p => p.Add(v => v.Group, colored).Add(v => v.Selection, _selection));

        view.Find("[data-option=\"Status eq 'Open'\"] .cw-facet-swatch-dot").GetAttribute("style").ShouldContain("#00838f");
        view.Find("[data-option=\"Status eq 'Shipped'\"] .cw-facet-swatch-dot").GetAttribute("style").ShouldContain("background:#");
        view.Find("[data-option=\"Status eq 'Open'\"]").Click();
        _selection.ToFilter().ShouldBe("Status eq 'Open'");
    }

    [Fact]
    public void The_tree_chooses_every_value_below_a_node()
    {
        var mime = Group("mime", "File type", FacetValueKind.Text,
            Option("Mime eq 'application/pdf'", "application/pdf", 4), Option("Mime eq 'image/png'", "image/png", 2), Option("Mime eq 'image/jpeg'", "image/jpeg", 1))
            with { TreeSeparator = "/" };
        var view = Render<FacetTreeView>(p => p.Add(v => v.Group, mime).Add(v => v.Selection, _selection));

        view.FindAll("[data-node]").Select(n => n.GetAttribute("data-node")).ShouldBe(["application", "image"]);
        view.Find("[data-node='image'] .cw-facet-count").TextContent.ShouldBe("3");
        view.Find("[data-node='image']").Click();
        _selection.ToFilter().ShouldBe("(Mime eq 'image/png' or Mime eq 'image/jpeg')");

        view.Find("[data-node='image'] [data-testid='facet-twist']").Click();
        view.Find("[data-node='png']").Click();
        _selection.ToFilter().ShouldBe("Mime eq 'image/jpeg'");
        view.Find("[data-node='image'] .cw-facet-check").OuterHtml.ShouldNotBeEmpty();
    }

    [Fact]
    public void The_switch_filters_yes_no_or_all()
    {
        var paid = Group("paid", "Paid", FacetValueKind.Boolean,
            new FacetOptionDto(JsonSerializer.SerializeToElement("false"), "No", true, 4, "Paid eq false"),
            new FacetOptionDto(JsonSerializer.SerializeToElement("true"), "Yes", true, 6, "Paid eq true"));
        var view = Render<FacetToggleView>(p => p.Add(v => v.Group, paid).Add(v => v.Selection, _selection));

        view.FindAll("[data-option]").Select(b => b.GetAttribute("data-option")).ShouldBe(["", "Paid eq true", "Paid eq false"]);
        view.Find("[data-option='']").TextContent.ShouldContain("10");
        view.Find("[data-option='Paid eq true']").Click();
        _selection.ToFilter().ShouldBe("Paid eq true");
        view.Find("[data-option='Paid eq false']").Click();
        _selection.ToFilter().ShouldBe("Paid eq false");
        view.Find("[data-option='']").Click();
        _selection.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void The_range_picks_buckets_types_and_slides()
    {
        var total = Group("total", "Total", FacetValueKind.Number,
            Bucket("(Total ge 10 and Total lt 40)", "10", "40", 5), Bucket("(Total ge 40 and Total lt 70)", "40", "70", 2), Bucket("(Total ge 70 and Total le 100)", "70", "100", 1))
            with { Type = FacetKind.Range, Min = "10", Max = "100" };
        var view = Render<FacetRangeView>(p => p.Add(v => v.Group, total).Add(v => v.Selection, _selection));

        view.FindAll(".cw-facet-histogram-bar").Count.ShouldBe(3);
        view.Find("[data-bucket='(Total ge 10 and Total lt 40)']").Click();
        _selection.ToFilter().ShouldBe("(Total ge 10 and Total le 40)");
        view.Find("[data-bucket='(Total ge 10 and Total lt 40)']").ClassList.ShouldContain("active");

        view.Find("[data-testid='facet-range-max'] input, input[data-testid='facet-range-max']").Change("100");
        view.Find("[data-testid='facet-range-min'] input, input[data-testid='facet-range-min']").Change("10");
        _selection.IsEmpty.ShouldBeTrue("the whole range is no choice");

        var from = view.Find("[data-testid='facet-range-slider'] .mud-ex-range-slider-thumb.start");
        from.GetAttribute("role").ShouldBe("slider");
        from.KeyDown(new KeyboardEventArgs { Key = "PageUp" });
        view.Find("[data-testid='facet-range-slider'] .mud-ex-range-slider-thumb.start").KeyDown(new KeyboardEventArgs { Key = "PageUp" });
        view.Find("[data-testid='facet-range-slider'] .mud-ex-range-slider-thumb.start").KeyDown(new KeyboardEventArgs { Key = "PageUp" });
        _selection.IsEmpty.ShouldBeTrue("sliding applies once the thumbs rest");
        view.WaitForAssertion(() => _selection.ToFilter().ShouldBe("(Total ge 40 and Total le 100)"));
        _selection.All.Single().Label.ShouldContain("40");
    }

    [Fact]
    public void Dates_offer_relative_presets_buckets_and_a_custom_range()
    {
        Render<MudPopoverProvider>();
        var date = Group("orderDate", "Order date", FacetValueKind.Date,
            Bucket("(OrderDate ge 2026-09-01T00:00:00Z and OrderDate lt 2026-10-01T00:00:00Z)", "2026-09-01", "2026-10-01", 3, "month"),
            Bucket("(OrderDate ge 2026-10-01T00:00:00Z and OrderDate lt 2026-11-01T00:00:00Z)", "2026-10-01", "2026-11-01", 5, "month"))
            with { Type = FacetKind.DateRange, ValuePath = "OrderDate", Min = "2026-09-03", Max = "2026-10-20" };
        var view = Render<FacetDateView>(p => p.Add(v => v.Group, date).Add(v => v.Selection, _selection));

        view.Find("[data-preset='today']").Click();
        _selection.ToFilter().ShouldBe("(OrderDate ge 2026-10-11T00:00:00+02:00 and OrderDate lt 2026-10-12T00:00:00+02:00)");
        _selection.All.Single().Preset.ShouldBe("today");
        _selection.Now = () => Now.AddDays(1);
        _selection.ToFilter().ShouldBe("(OrderDate ge 2026-10-12T00:00:00+02:00 and OrderDate lt 2026-10-13T00:00:00+02:00)", "presets stay relative");

        view.Find("[data-bucket^='(OrderDate ge 2026-09-01']").Click();
        _selection.ToFilter().ShouldBe(date.Options[0].OData);

        view.Find("input[data-testid='facet-date-from'], [data-testid='facet-date-from'] input").Change(new DateTime(2026, 10, 1).ToShortDateString());
        view.Find("input[data-testid='facet-date-to'], [data-testid='facet-date-to'] input").Change(new DateTime(2026, 10, 3).ToShortDateString());
        view.Find("[data-testid='facet-date-apply']").Click();
        view.WaitForAssertion(() => _selection.ToFilter().ShouldStartWith("(OrderDate ge 2026-10-01T00:00:00+02:00 and OrderDate lt 2026-10-04T00:00:00"));

        var to = view.Find("[data-testid='facet-date-slider'] .mud-ex-range-slider-thumb.end");
        to.GetAttribute("aria-valuenow").ShouldBe(new DateOnly(2026, 10, 3).ToString("d"));
        to.KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        view.Find("[data-testid='facet-date-slider'] .mud-ex-range-slider-thumb.start").KeyDown(new KeyboardEventArgs { Key = "Home" });
        view.WaitForAssertion(() => _selection.ToFilter().ShouldStartWith("(OrderDate ge 2026-09-03T00:00:00+02:00 and OrderDate lt 2026-10-05T00:00:00"));
    }

    [Fact]
    public void A_switchable_group_changes_any_of_to_all_of_and_its_view()
    {
        var options = Group("options", "Options", FacetValueKind.Flags,
            Option("Options has 'Express'", "Express", 3), Option("Options has 'Insured'", "Insured", 2)) with { Switchable = true };
        FacetView? picked = null;
        var view = Render<FacetGroupView>(p => p.Add(v => v.Group, options).Add(v => v.Selection, _selection).Add(v => v.ViewChanged, v => picked = v));

        view.Find("[data-facet-group='options']").GetAttribute("data-view").ShouldBe("Chips");
        view.Find("[data-option=\"Options has 'Express'\"]").Click();
        view.Find("[data-option=\"Options has 'Insured'\"]").Click();
        _selection.ToFilter().ShouldBe("(Options has 'Express' or Options has 'Insured')");
        view.Find("[data-combine='all']").Click();
        _selection.ToFilter().ShouldBe("(Options has 'Express' and Options has 'Insured')");

        view.Render(p => p.Add(v => v.View, FacetView.Checkbox));
        view.Find("[data-facet-group='options']").GetAttribute("data-view").ShouldBe("Checkbox");
        view.FindAll(".cw-facet-option.selected").Count.ShouldBe(2);
        picked.ShouldBeNull();
        view.Find("[data-testid='facet-clear']").Click();
        _selection.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Views_follow_the_values_unless_set()
    {
        Facets.View(Status).ShouldBe(FacetView.Chips);
        Facets.View(Regions).ShouldBe(FacetView.Checkbox);
        Facets.View(Group("paid", "Paid", FacetValueKind.Boolean)).ShouldBe(FacetView.Toggle);
        Facets.View(Group("total", "Total", FacetValueKind.Number) with { Type = FacetKind.Range }).ShouldBe(FacetView.Range);
        Facets.View(Group("d", "D", null) with { Type = FacetKind.DateRange }).ShouldBe(FacetView.Dates);
        Facets.View(Status with { View = FacetView.Select }).ShouldBe(FacetView.Select);
        Facets.View(Status, FacetView.Swatches).ShouldBe(FacetView.Swatches);
        Facets.View(Status, FacetView.Range).ShouldBe(FacetView.Chips, "a view that does not fit is ignored");
        Facets.Combine(Status with { Combine = null, MultiSelect = false }).ShouldBe(FacetCombine.Single);
        Facets.Combine(Status with { Combine = null, GroupOperator = FacetOperator.And }).ShouldBe(FacetCombine.All);
        var status = Status;
        new FacetSettings { View = FacetView.Buttons, AllowExclude = false }.Apply(status).ShouldBe(status with { View = FacetView.Buttons, AllowExclude = false });
    }

    [Fact]
    public void The_bar_shows_the_choice_on_its_pill_and_the_panel_opens_groups()
    {
        var popovers = Render<MudPopoverProvider>();
        _selection.Toggle(Status, Status.Options[1]);
        _selection.Toggle(Status, Status.Options[0]);
        var bar = Render<FacetBar>(p => p.Add(b => b.Groups, [Status, Regions]).Add(b => b.Selection, _selection));

        var pill = bar.Find("[data-facet='status'] [data-testid='facet-button']");
        pill.ClassList.ShouldContain("active");
        pill.TextContent.ShouldContain("On the way");
        pill.QuerySelector(".cw-facet-badge")!.TextContent.ShouldBe("2");
        pill.Click();
        popovers.WaitForElement("[data-facet-group='status']");

        var panel = Render<FacetPanel>(p => p.Add(b => b.Groups, [Status, Regions]).Add(b => b.Selection, _selection).Add(b => b.OpenFirst, 0));
        panel.FindAll("[data-facet-group]").Select(g => g.GetAttribute("data-facet-group")).ShouldBe(["status"], "groups with a choice open");
        panel.Find("[data-facet='region'] .cw-facet-section-toggle").Click();
        panel.FindAll("[data-facet-group]").Count.ShouldBe(2);
    }

    [Fact]
    public void Selections_round_trip_through_the_state_with_exclusions_presets_and_all_of()
    {
        var options = Group("options", "Options", FacetValueKind.Flags, Option("Options has 'Express'", "Express", 3), Option("Options has 'Insured'", "Insured", 2));
        var date = Group("orderDate", "Order date", FacetValueKind.Date) with { Type = FacetKind.DateRange, ValuePath = "OrderDate" };
        _selection.Toggle(Status, Status.Options[0], exclude: true);
        _selection.SetCombine(options, FacetCombine.All);
        _selection.Toggle(options, options.Options[0]);
        _selection.Toggle(options, options.Options[1]);
        _selection.Set(date, "Last 7 days", "x", "last7");
        var filter = _selection.ToFilter();

        var state = DataTableState.Decode(new DataTableState(null, [.. _selection.All], []).Encode())!;
        var restored = new FacetSelection { Now = () => Now };
        restored.Restore(state.Facets);

        restored.ToFilter().ShouldBe(filter);
        filter.ShouldContain("not (Status eq 'Open')");
        filter.ShouldContain("(Options has 'Express' and Options has 'Insured')");
        filter.ShouldContain("OrderDate ge 2026-10-05T00:00:00+02:00");
        restored.CombineOf(options).ShouldBe(FacetCombine.All);
        DataTableState.Decode(new DataTableState(null, [new SelectedFacet("a", "A", FacetOperator.Or, "x", "A eq 1")], []).Encode())!.Facets[0].Exclude.ShouldBeFalse();
    }

    [Fact]
    public void The_context_leaves_each_group_out_of_its_own_counts()
    {
        var options = Group("options", "Options", FacetValueKind.Flags, Option("Options has 'Express'", "Express", 3)) with { Combine = FacetCombine.All };
        _selection.Toggle(Status, Status.Options[0]);
        _selection.Toggle(options, options.Options[0]);

        var context = _selection.Context("Total gt 5", [Status, options])!;

        context.Groups.Keys.ShouldBe(["status", "options"], ignoreOrder: true);
        context.Combines!["options"].ShouldBe(FacetCombine.All);
        context.Without("status").ShouldBe("(Total gt 5) and (Options has 'Express')");
        FacetContextDto.Decode(context.Encode())!.Without("options").ShouldBe("(Total gt 5) and (Status eq 'Open')");
        new FacetSelection().Context("x", []).ShouldBeNull();
    }

    private static FacetGroupDto Group(string key, string label, FacetValueKind? kind, params FacetOptionDto[] options) =>
        new(key, label, label, FacetKind.CheckboxList, FacetOperator.Or, options, true, 0) { ValueKind = kind, ValuePath = label.Replace(" ", string.Empty) };

    private static FacetOptionDto Option(string odata, string label, int count) => new(JsonSerializer.SerializeToElement(label), label, count > 0, count, odata);

    private static FacetOptionDto Bucket(string odata, string from, string to, int count, string unit = "number") =>
        new(JsonSerializer.SerializeToElement(from), from, count > 0, count, odata) { Meta = new Dictionary<string, string> { ["from"] = from, ["to"] = to, ["unit"] = unit } };
}

internal static class ElementExtensions
{
    public static void ContextMenu(this IElement element) => element.TriggerEvent("oncontextmenu", new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
}
