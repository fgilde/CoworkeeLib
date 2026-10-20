using Microsoft.OData.UriParser;

namespace Coworkee.Reporting.Sources;

/// <summary>Reads the date range a filter asks for: <c>ge</c>/<c>gt</c> and <c>lt</c>/<c>le</c> on a date field in its top-level <c>and</c> chain.</summary>
internal static class ReportSourceDates
{
    public static (DateOnly? From, DateOnly? To) Bounds(FilterClause? filter)
    {
        DateOnly? from = null, to = null;
        Visit(filter?.Expression);
        return (from, to);

        void Visit(QueryNode? node)
        {
            if (node is not BinaryOperatorNode binary)
            {
                return;
            }

            if (binary.OperatorKind == BinaryOperatorKind.And)
            {
                Visit(binary.Left);
                Visit(binary.Right);
                return;
            }

            if (Unwrap(binary.Left) is not SingleValuePropertyAccessNode || (Unwrap(binary.Right) as ConstantNode)?.Value is not { } value)
            {
                return;
            }

            if (binary.OperatorKind is BinaryOperatorKind.GreaterThanOrEqual or BinaryOperatorKind.GreaterThan && Day(value, upper: false) is { } low)
            {
                from = from is { } f && f > low ? f : low;
            }
            else if (binary.OperatorKind is BinaryOperatorKind.LessThan or BinaryOperatorKind.LessThanOrEqual
                && Day(value, upper: true, binary.OperatorKind == BinaryOperatorKind.LessThanOrEqual) is { } high)
            {
                to = to is { } t && t < high ? t : high;
            }
        }
    }

    private static QueryNode Unwrap(QueryNode node) => node is ConvertNode convert ? Unwrap(convert.Source) : node;

    /// <summary>The day of a bound; an upper bound that includes its day (le) or has a time after midnight ends the day after.</summary>
    private static DateOnly? Day(object value, bool upper, bool inclusive = false)
    {
        var (day, time) = value switch
        {
            DateTimeOffset v => (DateOnly.FromDateTime(v.UtcDateTime), v.UtcDateTime.TimeOfDay),
            DateTime v => (DateOnly.FromDateTime(v), v.TimeOfDay),
            DateOnly v => (v, TimeSpan.Zero),
            Microsoft.OData.Edm.Date v => (new DateOnly(v.Year, v.Month, v.Day), TimeSpan.Zero),
            _ => ((DateOnly?)null, TimeSpan.Zero),
        };
        return day is { } d && upper && (inclusive || time > TimeSpan.Zero) ? d.AddDays(1) : day;
    }
}
