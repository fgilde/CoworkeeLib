namespace Coworkee.Client.Blazor.Components.Calendar;

/// <summary>A row of the <see cref="ResourceCalendar"/>: a person, a room, a machine. Rows with the same <see cref="Group"/> share a header.</summary>
public sealed record CalendarResource(string Id, string Title, string? Subtitle = null, Guid? UserId = null, string? Group = null);

/// <summary>
/// A bar from <see cref="Start"/> to <see cref="End"/> (both inclusive) in the row of <see cref="ResourceId"/>.
/// <see cref="HalfStart"/> begins it at noon of the first day, <see cref="HalfEnd"/> ends it at noon of the last; <see cref="Tentative"/> draws it hatched.
/// </summary>
public sealed record CalendarItem(
    string ResourceId, DateOnly Start, DateOnly End, string Title, string Color,
    bool HalfStart = false, bool HalfEnd = false, bool Tentative = false, string? Icon = null, object? Data = null);

/// <summary>A day drawn shaded for every row, e.g. a public holiday; <see cref="ResourceId"/> limits it to one row.</summary>
public sealed record CalendarDayMark(DateOnly Date, string Label, string? ResourceId = null);

/// <summary>Days a user dragged over in one row.</summary>
public sealed record CalendarRange(CalendarResource Resource, DateOnly Start, DateOnly End);

public enum CalendarSpan
{
    Week,
    TwoWeeks,
    Month,
}
