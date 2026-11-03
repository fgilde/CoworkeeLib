using Coworkee.Contracts.Reporting;

namespace Coworkee.Client.Blazor.Reporting.Widgets;

public sealed record ChartPoint(string Label, decimal? Value, ReportKey? Key, decimal? X = null)
{
    /// <summary>The first day of a date bucket, for time axes.</summary>
    public DateTime? Date => DateTime.TryParseExact(Key?.Value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var day) ? day : null;
}
