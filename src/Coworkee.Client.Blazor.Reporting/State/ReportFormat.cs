using System.Globalization;
using System.Text.RegularExpressions;
using Coworkee.Contracts.Reporting;

namespace Coworkee.Client.Blazor.Reporting.State;

/// <summary>Numbers and keys as widgets show them, in the user's culture.</summary>
public static partial class ReportFormat
{
    public static string Number(double? value, WidgetOptions options, CultureInfo? culture = null) => ReportNumbers.Format(value, options, culture);

    /// <summary>A JavaScript formatter for chart axes and tooltips, with Intl.NumberFormat in the user's culture.</summary>
    public static string Script(WidgetOptions options, CultureInfo? culture = null, bool compact = false)
    {
        var locale = (culture ?? CultureInfo.CurrentCulture).Name is { Length: > 0 } name ? name : "en";
        var style = options.Format switch
        {
            NumberFormat.Currency => $"{{style:'currency',currency:'{Currency(options)}',maximumFractionDigits:0}}",
            NumberFormat.Percent => "{style:'percent',maximumFractionDigits:1}",
            NumberFormat.Compact => "{notation:'compact',maximumFractionDigits:1}",
            NumberFormat.Integer => "{maximumFractionDigits:0}",
            _ => "{maximumFractionDigits:2}",
        };
        if (compact && options.Format != NumberFormat.Percent)
        {
            style = options.Format == NumberFormat.Currency
                ? $"{{style:'currency',currency:'{Currency(options)}',notation:'compact',maximumFractionDigits:1}}"
                : "{notation:'compact',maximumFractionDigits:1}";
        }

        var value = options.Format == NumberFormat.Percent ? "v/100" : "v";
        return $"function(v){{ return v == null || isNaN(v) ? '' : new Intl.NumberFormat('{locale}',{style}).format({value}); }}";
    }

    public static string Key(ReportKey? key, DateBucket bucket, ReportFieldDto? field, Func<string, string> localize, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        if (key is null)
        {
            return string.Empty;
        }

        if (key.Value is null)
        {
            return localize("(empty)");
        }

        if (field?.Kind == ReportFieldKind.Date && DateOnly.TryParseExact(key.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            return bucket switch
            {
                DateBucket.Year => day.Year.ToString(culture),
                DateBucket.Quarter => $"Q{(day.Month + 2) / 3} {day.Year}",
                DateBucket.Month => day.ToString("MMM yy", culture),
                DateBucket.Week => day.ToString("d MMM yy", culture),
                _ => day.ToString("d", culture),
            };
        }

        if (field?.Kind == ReportFieldKind.Boolean)
        {
            return localize(key.Value == "true" ? "Yes" : "No");
        }

        return field?.Kind == ReportFieldKind.Enum ? localize(key.Label) : key.Label;
    }

    public static string AggregateLabel(ReportAggregate aggregate, ReportFieldDto? field, Localization.CoworkeeLocalizer l) =>
        aggregate.Label is { Length: > 0 } label ? l.Stored(label)!
        : aggregate.Function == AggregateFunction.Count ? l["Count"]
        : $"{l[Humanize(aggregate.Function.ToString())]} {field?.Label ?? aggregate.Field}";

    /// <summary>"Last12Months" becomes "Last 12 months", "InProgress" "In progress".</summary>
    public static string Humanize(string name)
    {
        var words = Words().Split(name).Where(w => w.Length > 0).ToList();
        return string.Join(' ', words.Select((w, i) => i == 0 ? w : w.ToLowerInvariant()));
    }

    private static string Currency(WidgetOptions options) => ReportNumbers.Currency(options);

    [GeneratedRegex("(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Za-z])(?=[0-9])")]
    private static partial Regex Words();
}
