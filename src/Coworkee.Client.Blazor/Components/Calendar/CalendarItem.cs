namespace Coworkee.Client.Blazor.Components.Calendar;

/// <summary>
/// A bar from <see cref="Start"/> to <see cref="End"/> (both inclusive) in the row of <see cref="ResourceId"/>.
/// <see cref="HalfStart"/> begins it at noon of the first day, <see cref="HalfEnd"/> ends it at noon of the last; <see cref="Tentative"/> draws it hatched.
/// </summary>
public sealed record CalendarItem(
    string ResourceId, DateOnly Start, DateOnly End, string Title, string Color,
    bool HalfStart = false, bool HalfEnd = false, bool Tentative = false, string? Icon = null, object? Data = null);
