using Bunit;
using Coworkee.Client.Blazor.Components.Calendar;

namespace Coworkee.Client.Blazor.Tests;

public sealed class ResourceCalendarTests : ClientTestBase
{
    private static readonly DateOnly March = new(2026, 3, 1);

    private static readonly CalendarResource Ann = new("ann", "Ann Lee", "Sales", Group: "Sales");

    private static readonly CalendarResource Bob = new("bob", "Bob Ray", Group: "Sales");

    [Fact]
    public void Overlapping_items_stack_in_lanes_and_half_days_start_at_noon()
    {
        var cut = Render<ResourceCalendar>(p => p
            .Add(c => c.Resources, [Ann, Bob])
            .Add(c => c.From, March)
            .Add(c => c.Items, [
                new CalendarItem("ann", new(2026, 3, 2), new(2026, 3, 4), "Vacation", "#22c55e", HalfStart: true),
                new CalendarItem("ann", new(2026, 3, 3), new(2026, 3, 3), "Training", "#0ea5e9"),
                new CalendarItem("bob", new(2026, 2, 20), new(2026, 3, 1), "Sick", "#ef4444", Tentative: true),
            ]));

        cut.Find("[data-testid=calendar-title]").TextContent.ShouldBe("March 2026");
        cut.FindAll("[data-testid=calendar-group]").Select(g => g.TextContent).ShouldBe(["Sales"]);
        var bars = cut.FindAll("[data-testid=calendar-item]");
        // column 2 is the morning of the 1st: the 2nd starts at 4, noon at 5; the 4th ends after column 9
        bars[0].GetAttribute("style")!.ShouldStartWith("grid-column:5/10;grid-row:1;");
        bars[1].GetAttribute("style")!.ShouldStartWith("grid-column:6/8;grid-row:2;");
        bars[2].GetAttribute("style")!.ShouldContain("grid-column:2/4;");
        bars[2].GetAttribute("style")!.ShouldContain("repeating-linear-gradient");
    }

    [Fact]
    public void Dragging_over_days_of_a_row_reports_the_range_and_the_toolbar_moves_by_the_span()
    {
        CalendarRange? range = null;
        var cut = Render<ResourceCalendar>(p => p
            .Add(c => c.Resources, [Ann])
            .Add(c => c.From, March)
            .Add(c => c.Marks, [new CalendarDayMark(new(2026, 3, 6), "Holiday")])
            .Add(c => c.OnRangeSelected, (CalendarRange r) => range = r));

        var days = cut.FindAll("[data-testid=calendar-row] .cw-cal-day");
        days[9].MouseDown();
        cut.FindAll("[data-testid=calendar-row] .cw-cal-day")[5].MouseEnter();
        cut.Find(".cw-cal-scroll").MouseUp();

        range.ShouldBe(new CalendarRange(Ann, new(2026, 3, 6), new(2026, 3, 10)));
        cut.FindAll("[data-testid=calendar-row] .cw-cal-day")[5].ClassList.ShouldContain("cw-cal-mark");

        cut.Find("[data-testid=calendar-next]").Click();
        cut.Find("[data-testid=calendar-title]").TextContent.ShouldBe("April 2026");
    }

    [Fact]
    public void A_week_starts_on_the_first_day_of_the_culture()
    {
        ResourceCalendar.Start(new DateOnly(2026, 3, 5), CalendarSpan.Week).ShouldBe(new DateOnly(2026, 3, 1));
        ResourceCalendar.Start(new DateOnly(2026, 3, 5), CalendarSpan.Month).ShouldBe(March);
    }
}
