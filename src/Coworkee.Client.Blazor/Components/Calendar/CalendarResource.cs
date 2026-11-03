namespace Coworkee.Client.Blazor.Components.Calendar;

/// <summary>
/// A row of the <see cref="ResourceCalendar"/>: a person, a room, a machine. Rows with the same <see cref="Group"/> share a header.
/// <see cref="WorkDays"/> replaces the calendar's work days for this row, e.g. of a part-time employee.
/// </summary>
public sealed record CalendarResource(string Id, string Title, string? Subtitle = null, Guid? UserId = null, string? Group = null, IReadOnlySet<DayOfWeek>? WorkDays = null);
