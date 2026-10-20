using System.Globalization;
using System.Text.RegularExpressions;

namespace Coworkee.Contracts.Reporting;

/// <summary>A widget's number as text in a culture, the same in the browser and in mails.</summary>
public static partial class ReportNumbers
{
    public static string Format(double? value, WidgetOptions options, CultureInfo? culture = null)
    {
        if (value is not { } number)
        {
            return "–";
        }

        culture ??= CultureInfo.CurrentCulture;
        return options.Format switch
        {
            NumberFormat.Integer => Math.Round(number).ToString("N0", culture),
            NumberFormat.Percent => (number / 100).ToString("P1", culture),
            NumberFormat.Currency => Compact(number, culture, WithCurrency(culture, options).NumberFormat.CurrencySymbol) ?? number.ToString("C" + (Math.Abs(number) >= 1000 ? "0" : "2"), WithCurrency(culture, options)),
            NumberFormat.Compact => Compact(number, culture, null) ?? number.ToString("N0", culture),
            _ => number.ToString(Math.Abs(number) >= 100 || number == Math.Round(number) ? "N0" : "N2", culture),
        };
    }

    /// <summary>The ISO code of the widget's currency; anything that is no three letter code becomes EUR (the code ends up in scripts).</summary>
    public static string Currency(WidgetOptions options) => CurrencyCode().IsMatch(options.Currency ?? string.Empty) ? options.Currency!.ToUpperInvariant() : "EUR";

    private static CultureInfo WithCurrency(CultureInfo culture, WidgetOptions options)
    {
        var copy = (CultureInfo)culture.Clone();
        copy.NumberFormat.CurrencySymbol = Currency(options) switch
        {
            "EUR" => "€",
            "USD" => "$",
            "GBP" => "£",
            "JPY" => "¥",
            var code => code,
        };
        return copy;
    }

    private static string? Compact(double number, CultureInfo culture, string? currency)
    {
        var abs = Math.Abs(number);
        var (scaled, suffix) = abs >= 1e9 ? (number / 1e9, "B") : abs >= 1e6 ? (number / 1e6, "M") : abs >= 1e4 ? (number / 1e3, "K") : (0d, null);
        return suffix is null ? null : scaled.ToString(Math.Abs(scaled) >= 100 ? "N0" : "N1", culture) + suffix + (currency is null ? string.Empty : " " + currency);
    }

    [GeneratedRegex("^[A-Za-z]{3}$")]
    private static partial Regex CurrencyCode();
}
