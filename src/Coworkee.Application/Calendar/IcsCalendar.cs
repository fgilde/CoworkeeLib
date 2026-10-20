using System.Text;

namespace Coworkee.Application.Calendar;

/// <summary>
/// An iCalendar (RFC 5545) document: <c>new IcsCalendar { Name = "Absences" }.Add(events).ToString()</c> is the text a calendar app
/// imports or subscribes to.
/// </summary>
public sealed class IcsCalendar
{
    public string ProductId { get; init; } = "-//Coworkee//Calendar//EN";

    /// <summary>Display name for subscribing apps (X-WR-CALNAME).</summary>
    public string? Name { get; init; }

    /// <summary>How often subscribers should fetch again (REFRESH-INTERVAL and X-PUBLISHED-TTL).</summary>
    public TimeSpan? RefreshInterval { get; init; }

    public DateTimeOffset Stamp { get; init; } = DateTimeOffset.UtcNow;

    public List<IcsEvent> Events { get; } = [];

    public IcsCalendar Add(params IEnumerable<IcsEvent> events)
    {
        Events.AddRange(events);
        return this;
    }

    public override string ToString()
    {
        var ics = new StringBuilder();
        void Line(string line) => IcsText.AppendFolded(ics, line);
        void Text(string name, string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                Line($"{name}:{IcsText.Escape(value)}");
            }
        }

        Line("BEGIN:VCALENDAR");
        Line("VERSION:2.0");
        Text("PRODID", ProductId);
        Line("CALSCALE:GREGORIAN");
        Text("X-WR-CALNAME", Name);
        if (RefreshInterval is { } refresh)
        {
            var duration = $"PT{(int)refresh.TotalMinutes}M";
            Line($"REFRESH-INTERVAL;VALUE=DURATION:{duration}");
            Line($"X-PUBLISHED-TTL:{duration}");
        }

        foreach (var e in Events)
        {
            Line("BEGIN:VEVENT");
            Text("UID", e.Uid);
            Line(IcsTime.Utc(e.Stamp ?? Stamp).Property("DTSTAMP"));
            Line(e.Start.Property("DTSTART"));
            if (e.End is { } end)
            {
                Line(end.Property("DTEND"));
            }

            Text("SUMMARY", e.Summary);
            Text("DESCRIPTION", e.Description);
            Text("LOCATION", e.Location);
            if (e.Url is not null)
            {
                Line($"URL:{e.Url.AbsoluteUri}");
            }

            if (e.Status is { } status)
            {
                Line($"STATUS:{status.ToString().ToUpperInvariant()}");
            }

            if (e.Categories.Count > 0)
            {
                Line($"CATEGORIES:{string.Join(',', e.Categories.Select(IcsText.Escape))}");
            }

            if (e.Organizer is not null)
            {
                Line(e.Organizer.Property("ORGANIZER"));
            }

            foreach (var attendee in e.Attendees)
            {
                Line(attendee.Property("ATTENDEE"));
            }

            if (e.Transparent)
            {
                Line("TRANSP:TRANSPARENT");
            }

            Line("END:VEVENT");
        }

        Line("END:VCALENDAR");
        return ics.ToString();
    }
}
