using System.Text;
using Coworkee.Application.Calendar;

namespace Coworkee.Application.Tests;

public sealed class IcsCalendarTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 8, 30, 0, TimeSpan.FromHours(2));

    [Fact]
    public void Events_carry_dates_times_and_people()
    {
        var ics = new IcsCalendar { Name = "Team", Stamp = Now, RefreshInterval = TimeSpan.FromHours(1) }.Add(
            IcsEvent.AllDay("a1@hr", new DateOnly(2026, 12, 24), new DateOnly(2026, 12, 26), "Holidays") with { Transparent = true },
            new IcsEvent("t1@hr", IcsTime.InZone(new DateTime(2026, 11, 2, 9, 0, 0), "Europe/Berlin"), "Talk")
            {
                End = Now.AddHours(1),
                Location = "Room 1",
                Url = new Uri("https://hr.example/talks/1"),
                Status = IcsStatus.Confirmed,
                Categories = ["Talk", "1:1"],
                Organizer = new IcsPerson("lead@example.com", "Lead, Team"),
                Attendees = [new IcsPerson("anna@example.com", "Anna")],
            }).ToString();

        var lines = ics.Split("\r\n");
        lines[0].ShouldBe("BEGIN:VCALENDAR");
        lines.ShouldContain("X-WR-CALNAME:Team");
        lines.ShouldContain("REFRESH-INTERVAL;VALUE=DURATION:PT60M");
        lines.ShouldContain("DTSTAMP:20261020T063000Z");
        lines.ShouldContain("DTSTART;VALUE=DATE:20261224");
        lines.ShouldContain("DTEND;VALUE=DATE:20261227");
        lines.ShouldContain("TRANSP:TRANSPARENT");
        lines.ShouldContain("DTSTART;TZID=Europe/Berlin:20261102T090000");
        lines.ShouldContain("DTEND:20261020T073000Z");
        lines.ShouldContain("URL:https://hr.example/talks/1");
        lines.ShouldContain("STATUS:CONFIRMED");
        lines.ShouldContain("CATEGORIES:Talk,1:1");
        lines.ShouldContain("ORGANIZER;CN=\"Lead, Team\":mailto:lead@example.com");
        lines.ShouldContain("ATTENDEE;CN=Anna:mailto:anna@example.com");
        lines.Count(l => l == "BEGIN:VEVENT").ShouldBe(2);
        ics.ShouldEndWith("END:VCALENDAR\r\n");
    }

    [Fact]
    public void Text_escapes_separators_backslashes_and_newlines()
    {
        var ics = new IcsCalendar { Stamp = Now }.Add(new IcsEvent("x", Now, @"a,b;c\d") { Description = "one\r\ntwo\nthree" }).ToString();

        ics.ShouldContain(@"SUMMARY:a\,b\;c\\d" + "\r\n");
        ics.ShouldContain(@"DESCRIPTION:one\ntwo\nthree" + "\r\n");
    }

    [Fact]
    public void Long_lines_fold_at_75_octets_without_splitting_characters()
    {
        var text = string.Concat(Enumerable.Repeat("Grüße 😀 ", 30));
        var ics = new IcsCalendar { Stamp = Now }.Add(new IcsEvent("x", Now, text)).ToString();

        var bytes = Encoding.UTF8.GetBytes(ics);
        new UTF8Encoding(false, true).GetString(bytes).ShouldBe(ics);
        foreach (var line in ics.Split("\r\n"))
        {
            Encoding.UTF8.GetByteCount(line).ShouldBeLessThanOrEqualTo(75);
            char.IsLowSurrogate(line.FirstOrDefault(c => c != ' ')).ShouldBeFalse();
        }

        ics.Replace("\r\n ", "").ShouldContain($"SUMMARY:{text}\r\n");
        ics.Split("\r\n").Count(l => l.StartsWith(' ')).ShouldBeGreaterThan(3);
    }
}
