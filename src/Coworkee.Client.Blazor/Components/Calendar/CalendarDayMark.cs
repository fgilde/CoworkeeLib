namespace Coworkee.Client.Blazor.Components.Calendar;

/// <summary>A day drawn shaded for every row, e.g. a public holiday; <see cref="ResourceId"/> limits it to one row.</summary>
public sealed record CalendarDayMark(DateOnly Date, string Label, string? ResourceId = null);
