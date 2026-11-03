using Coworkee.Contracts.Reporting;

namespace Coworkee.Client.Blazor.Reporting.Widgets;

/// <summary>Query rows as series: one per value without series field, otherwise one per series key with the first value.</summary>
public static class ChartData
{
    public static List<ChartSeries> From(ReportQueryResult result, IReadOnlyList<string> valueNames, Func<ReportKey?, string> category, Func<ReportKey?, string> series, bool fillGaps)
    {
        if (result.Rows.Count == 0)
        {
            return [];
        }

        if (result.Rows[0].Series is null)
        {
            return [.. valueNames.Select((name, i) => new ChartSeries(name, [.. result.Rows.Select(r => new ChartPoint(category(r.Key), Value(r.Values, i), r.Key))]))];
        }

        var categories = result.Rows.Select(r => r.Key).DistinctBy(k => k?.Value).ToList();
        return
        [
            .. result.Rows.GroupBy(r => r.Series?.Value).Select(g =>
            {
                // duplicate category keys (labels that collapse, older servers) add up instead of throwing
                var values = g.GroupBy(r => r.Key?.Value ?? string.Empty)
                    .ToDictionary(k => k.Key, k => k.Select(r => Value(r.Values, 0)).Aggregate((decimal?)null, (sum, v) => v is null ? sum : (sum ?? 0) + v));
                return new ChartSeries(series(g.First().Series), [.. categories.Select(c => new ChartPoint(category(c), values.TryGetValue(c?.Value ?? string.Empty, out var v) ? v : fillGaps ? 0 : null, c))]);
            }),
        ];
    }

    /// <summary>Scatter: the first value along x, the second along y, one point per category.</summary>
    public static List<ChartSeries> Scatter(ReportQueryResult result, string name, Func<ReportKey?, string> category) =>
        [new ChartSeries(name, [.. result.Rows.Select(r => new ChartPoint(category(r.Key), Value(r.Values, r.Values.Count > 1 ? 1 : 0), r.Key, Value(r.Values, 0)))])];

    private static decimal? Value(IReadOnlyList<double?> values, int index) =>
        index < values.Count && values[index] is { } value && double.IsFinite(value) ? (decimal)value : null;
}
