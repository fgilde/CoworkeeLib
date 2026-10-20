using System.Globalization;

namespace Coworkee.Application.Calendar;

/// <summary>A point in time of an event: a whole day, an instant in UTC or a wall clock time in a named zone (IANA id).</summary>
public readonly record struct IcsTime
{
    private IcsTime(DateTime value, bool isDate, string? timeZone)
    {
        Value = value;
        IsDate = isDate;
        TimeZone = timeZone;
    }

    public DateTime Value { get; }

    public bool IsDate { get; }

    public string? TimeZone { get; }

    public static IcsTime Date(DateOnly day) => new(day.ToDateTime(TimeOnly.MinValue), true, null);

    public static IcsTime Utc(DateTimeOffset instant) => new(instant.UtcDateTime, false, null);

    public static IcsTime InZone(DateTime wallClock, string timeZone) => new(DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified), false, timeZone);

    public static implicit operator IcsTime(DateOnly day) => Date(day);

    public static implicit operator IcsTime(DateTimeOffset instant) => Utc(instant);

    // ponytail: TZID without a VTIMEZONE block; Google, Outlook and Apple resolve IANA ids themselves, strict parsers want the block
    internal string Property(string name) => IsDate
        ? $"{name};VALUE=DATE:{Value.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}"
        : TimeZone is null
            ? $"{name}:{Value.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)}"
            : $"{name};TZID={IcsText.Parameter(TimeZone)}:{Value.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture)}";
}
