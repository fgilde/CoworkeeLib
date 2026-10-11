using System.Globalization;
using Coworkee.Contracts.Reporting;

namespace Coworkee.Client.Blazor.Reporting.State;

/// <summary>Turns a preset into days: From inclusive, To exclusive.</summary>
public static class DateRanges
{
    public static (DateOnly From, DateOnly To) Resolve(ReportDateRange range, DateOnly today)
    {
        var month = new DateOnly(today.Year, today.Month, 1);
        var quarter = new DateOnly(today.Year, ((today.Month - 1) / 3 * 3) + 1, 1);
        return range.Preset switch
        {
            DateRangePreset.Today => (today, today.AddDays(1)),
            DateRangePreset.Last7Days => (today.AddDays(-6), today.AddDays(1)),
            DateRangePreset.Last30Days => (today.AddDays(-29), today.AddDays(1)),
            DateRangePreset.Last90Days => (today.AddDays(-89), today.AddDays(1)),
            DateRangePreset.ThisMonth => (month, month.AddMonths(1)),
            DateRangePreset.LastMonth => (month.AddMonths(-1), month),
            DateRangePreset.ThisQuarter => (quarter, quarter.AddMonths(3)),
            DateRangePreset.ThisYear => (new DateOnly(today.Year, 1, 1), new DateOnly(today.Year + 1, 1, 1)),
            DateRangePreset.LastYear => (new DateOnly(today.Year - 1, 1, 1), new DateOnly(today.Year, 1, 1)),
            DateRangePreset.Last12Months => (month.AddMonths(-11), month.AddMonths(1)),
            _ => (range.From ?? DateOnly.MinValue, (range.To ?? DateOnly.MaxValue.AddDays(-1)).AddDays(1)),
        };
    }

    /// <summary>The range of the same length right before, for trends.</summary>
    public static (DateOnly From, DateOnly To) Previous((DateOnly From, DateOnly To) range)
    {
        var months = (range.To.Year - range.From.Year) * 12 + range.To.Month - range.From.Month;
        return range.From.Day == 1 && range.To.Day == 1 && months > 0
            ? (range.From.AddMonths(-months), range.From)
            : (range.From.AddDays(range.From.DayNumber - range.To.DayNumber), range.From);
    }

    public static string Filter(string field, (DateOnly From, DateOnly To) range, bool dateOnly = false)
    {
        string Literal(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + (dateOnly ? string.Empty : "T00:00:00Z");
        var parts = new List<string>();
        if (range.From != DateOnly.MinValue)
        {
            parts.Add($"{field} ge {Literal(range.From)}");
        }

        if (range.To < DateOnly.MaxValue)
        {
            parts.Add($"{field} lt {Literal(range.To)}");
        }

        return string.Join(" and ", parts);
    }
}
