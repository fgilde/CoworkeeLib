using System.Globalization;
using Coworkee.Contracts.Reporting;

namespace Coworkee.Reporting.Querying;

/// <summary>Turns a group key into its text, label and the OData filter that selects its rows.</summary>
internal static class ReportKeys
{
    public static ReportKey For(ReportGroup group, object? value)
    {
        var path = group.Field.Path;
        if (value is null)
        {
            return new ReportKey(null, string.Empty, $"{path} eq null");
        }

        if (group.Field.Kind == ReportFieldKind.Date)
        {
            var key = Convert.ToInt32(value, CultureInfo.InvariantCulture);
            key = group.WeekIndex ? DateBuckets.DayOfWeekIndex(key) : key;
            var (from, to) = DateBuckets.Range(key, group.Bucket);
            return new ReportKey(from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), DateBuckets.Label(key, group.Bucket),
                $"{path} ge {DateLiteral(from, group.Field.ClrType)} and {path} lt {DateLiteral(to, group.Field.ClrType)}");
        }

        var (text, literal) = value switch
        {
            Enum member => (member.ToString(), $"{group.Field.EnumType}'{member}'"),
            string s => (s, "'" + s.Replace("'", "''", StringComparison.Ordinal) + "'"),
            bool b => (b ? "true" : "false", b ? "true" : "false"),
            Guid g => (g.ToString(), g.ToString()),
            IFormattable number => (number.ToString(null, CultureInfo.InvariantCulture), number.ToString(null, CultureInfo.InvariantCulture)),
            _ => (value.ToString()!, "'" + value + "'"),
        };
        return new ReportKey(text, value is Enum e ? Contracts.Data.EnumLabels.Of(e) : text, $"{path} eq {literal}");
    }

    private static string DateLiteral(DateOnly day, Type type) =>
        (Nullable.GetUnderlyingType(type) ?? type) == typeof(DateOnly)
            ? day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T00:00:00Z";
}
