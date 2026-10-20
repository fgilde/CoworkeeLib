namespace Coworkee.Application.Calendar;

/// <summary>One VEVENT. <see cref="End"/> is exclusive: an all-day event of one day ends on the next day (see <see cref="AllDay"/>).</summary>
public sealed record IcsEvent(string Uid, IcsTime Start, string Summary)
{
    public IcsTime? End { get; init; }

    public string? Description { get; init; }

    public string? Location { get; init; }

    public Uri? Url { get; init; }

    public IcsStatus? Status { get; init; }

    public IReadOnlyList<string> Categories { get; init; } = [];

    public IcsPerson? Organizer { get; init; }

    public IReadOnlyList<IcsPerson> Attendees { get; init; } = [];

    /// <summary>Does not block the subscriber's time (TRANSP:TRANSPARENT), e.g. holidays or colleagues' absences.</summary>
    public bool Transparent { get; init; }

    /// <summary>When the event was last changed; the calendar's stamp when not set.</summary>
    public DateTimeOffset? Stamp { get; init; }

    /// <summary>Whole days from <paramref name="first"/> to <paramref name="last"/>, both included.</summary>
    public static IcsEvent AllDay(string uid, DateOnly first, DateOnly last, string summary) =>
        new(uid, first, summary) { End = last.AddDays(1) };
}
