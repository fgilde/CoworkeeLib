namespace Coworkee.Client.Blazor.Components.Calendar;

/// <summary>Days a user dragged over in one row.</summary>
public sealed record CalendarRange(CalendarResource Resource, DateOnly Start, DateOnly End);
