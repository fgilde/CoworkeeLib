using System.Globalization;
using Coworkee.Client.Blazor.Localization;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components.Calendar;

/// <summary>
/// Rows of resources against columns of days, with bars for the items: absences per person, shifts, room bookings.
/// Every day has two columns (morning, afternoon), so half days are exact. Dragging over free days of a row reports a <see cref="CalendarRange"/>.
/// </summary>
public partial class ResourceCalendar
{
    private (CalendarResource Resource, DateOnly From, DateOnly To)? _drag;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public IReadOnlyList<CalendarResource> Resources { get; set; } = [];

    [Parameter] public IReadOnlyList<CalendarItem> Items { get; set; } = [];

    [Parameter] public IReadOnlyList<CalendarDayMark> Marks { get; set; } = [];

    /// <summary>The first day shown; a month starts at its first day, a week at the culture's first day of the week.</summary>
    [Parameter] public DateOnly From { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    [Parameter] public EventCallback<DateOnly> FromChanged { get; set; }

    [Parameter] public CalendarSpan Span { get; set; } = CalendarSpan.Month;

    [Parameter] public EventCallback<CalendarSpan> SpanChanged { get; set; }

    /// <summary>Days drawn as working days; the others are shaded like weekends.</summary>
    [Parameter] public IReadOnlySet<DayOfWeek> WorkDays { get; set; } = new HashSet<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };

    [Parameter] public bool ShowToolbar { get; set; } = true;

    [Parameter] public RenderFragment? ToolbarContent { get; set; }

    [Parameter] public RenderFragment<CalendarResource>? ResourceTemplate { get; set; }

    [Parameter] public string ResourceHeader { get; set; } = string.Empty;

    [Parameter] public EventCallback<CalendarItem> OnItemClick { get; set; }

    /// <summary>Set to let users drag over days of a row; the range never starts on an item.</summary>
    [Parameter] public EventCallback<CalendarRange> OnRangeSelected { get; set; }

    [Parameter] public string? Class { get; set; }

    [Parameter] public string Height { get; set; } = "auto";

    private DateOnly Today { get; } = DateOnly.FromDateTime(DateTime.Today);

    private DateOnly First => Start(From, Span);

    private int DayCount => Span switch
    {
        CalendarSpan.Week => 7,
        CalendarSpan.TwoWeeks => 14,
        _ => DateTime.DaysInMonth(First.Year, First.Month),
    };

    private IEnumerable<DateOnly> Days => Enumerable.Range(0, DayCount).Select(First.AddDays);

    private DateOnly Last => First.AddDays(DayCount - 1);

    private string Title => Span == CalendarSpan.Month
        ? First.ToString("MMMM yyyy", CultureInfo.CurrentCulture)
        : $"{First.ToString("d. MMM", CultureInfo.CurrentCulture)} – {Last.ToString("d. MMM yyyy", CultureInfo.CurrentCulture)}";

    internal static DateOnly Start(DateOnly day, CalendarSpan span)
    {
        if (span == CalendarSpan.Month)
        {
            return new DateOnly(day.Year, day.Month, 1);
        }

        var first = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        return day.AddDays(-(((int)day.DayOfWeek - (int)first + 7) % 7));
    }

    /// <summary>The bars of a row in lanes, so overlapping items stack instead of covering each other.</summary>
    internal static IReadOnlyList<(CalendarItem Item, int Lane)> Lanes(IEnumerable<CalendarItem> items)
    {
        var ends = new List<(DateOnly End, bool Half)>();
        var placed = new List<(CalendarItem, int)>();
        foreach (var item in items.OrderBy(i => i.Start).ThenBy(i => i.HalfStart).ThenByDescending(i => i.End))
        {
            var lane = ends.FindIndex(e => e.End < item.Start || (e.End == item.Start && e.Half && item.HalfStart));
            if (lane < 0)
            {
                lane = ends.Count;
                ends.Add(default);
            }

            ends[lane] = (item.End, item.HalfEnd);
            placed.Add((item, lane));
        }

        return placed;
    }

    private IEnumerable<IGrouping<string?, CalendarResource>> Groups => Resources.GroupBy(r => r.Group);

    private IReadOnlyList<(CalendarItem Item, int Lane)> RowItems(CalendarResource resource) =>
        Lanes(Items.Where(i => i.ResourceId == resource.Id && i.End >= First && i.Start <= Last));

    // grid column of the morning half of a day; column 1 is the resource
    private int Column(DateOnly day) => 2 + (2 * (day.DayNumber - First.DayNumber));

    private string BarStyle(CalendarItem item, int lane)
    {
        var start = item.Start < First ? Column(First) : Column(item.Start) + (item.HalfStart ? 1 : 0);
        var end = item.End > Last ? Column(Last) + 2 : Column(item.End) + (item.HalfEnd ? 1 : 2);
        var background = item.Tentative
            ? $"repeating-linear-gradient(135deg, {item.Color} 0 6px, color-mix(in srgb, {item.Color} 55%, transparent) 6px 12px)"
            : item.Color;
        return $"grid-column:{start}/{end};grid-row:{lane + 1};background:{background};";
    }

    private string DayClass(DateOnly day, string? resourceId = null) =>
        string.Join(' ', new[]
        {
            "cw-cal-day",
            WorkDays.Contains(day.DayOfWeek) ? null : "cw-cal-off",
            Mark(day, resourceId) is null ? null : "cw-cal-mark",
            day == Today ? "cw-cal-today" : null,
            InDrag(resourceId, day) ? "cw-cal-drag" : null,
        }.Where(c => c is not null));

    private CalendarDayMark? Mark(DateOnly day, string? resourceId) =>
        Marks.FirstOrDefault(m => m.Date == day && (m.ResourceId is null || m.ResourceId == resourceId));

    private bool InDrag(string? resourceId, DateOnly day) =>
        _drag is { } drag && drag.Resource.Id == resourceId && day >= Min(drag.From, drag.To) && day <= Max(drag.From, drag.To);

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;

    private void DragStart(CalendarResource resource, DateOnly day)
    {
        if (OnRangeSelected.HasDelegate)
        {
            _drag = (resource, day, day);
        }
    }

    private void DragOver(CalendarResource resource, DateOnly day)
    {
        if (_drag is { } drag && drag.Resource.Id == resource.Id)
        {
            _drag = drag with { To = day };
        }
    }

    private async Task DragEndAsync()
    {
        if (_drag is { } drag)
        {
            _drag = null;
            await OnRangeSelected.InvokeAsync(new CalendarRange(drag.Resource, Min(drag.From, drag.To), Max(drag.From, drag.To)));
        }
    }

    private Task MoveAsync(int direction) => SetFromAsync(Span == CalendarSpan.Month ? First.AddMonths(direction) : First.AddDays(direction * DayCount));

    private Task TodayAsync() => SetFromAsync(Start(Today, Span));

    private async Task SetFromAsync(DateOnly from)
    {
        From = from;
        await FromChanged.InvokeAsync(from);
    }

    private async Task SetSpanAsync(CalendarSpan span)
    {
        Span = span;
        From = Start(From, span);
        await SpanChanged.InvokeAsync(span);
        await FromChanged.InvokeAsync(From);
    }

    private static string Initials(string title) =>
        string.Concat(title.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpperInvariant(w[0])));

    private static string WeekDay(DateOnly day) => CultureInfo.CurrentCulture.DateTimeFormat.GetShortestDayName(day.DayOfWeek);
}
