using System.Globalization;
using System.Text;
using System.Text.Json;
using Coworkee.Contracts.Reporting;
using Coworkee.Reporting.Querying;

namespace Coworkee.Reporting.Ai;

/// <summary>The instructions that teach the model the report format and the data sets the user may read.</summary>
internal static class ReportPrompt
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    public static string Instructions(IReadOnlyList<ReportSource> sources, CultureInfo culture)
    {
        var text = new StringBuilder();
        text.AppendLine("You design dashboards (reports) for a business app. Answer with one JSON object, a ReportDefinition, and nothing else: no prose, no comments.");
        text.AppendLine($"Write titles, subtitles and labels in {culture.EnglishName} ({culture.Name}).");
        text.AppendLine();
        text.AppendLine("ReportDefinition: { widgets: ReportWidget[], filters: [], dateRange: { preset } | null, refreshSeconds: 0 }");
        text.AppendLine("ReportWidget: { id (short unique text), type, title, subtitle, x, y, w, h, query, options }");
        text.AppendLine($"type: {Names<WidgetType>()}. Markdown/Image/Embed have no query (options.text = markdown, options.url = https address).");
        text.AppendLine("Layout: a grid of 12 columns; x 0..11, w 1..12, x + w <= 12; y counts rows from 0; h 2..12. Widgets must not overlap. KPIs w 3 h 3, charts w 6 h 5, tables w 12 h 6.");
        text.AppendLine("query: { entitySet, filter (OData $filter or null), groupBy: { field, bucket } | null, splitBy (series, needs groupBy) | null, aggregates: [{ function, field, label }], dateField, sortBy, descending, top, columns (tables: field paths), orderBy }");
        text.AppendLine($"function: {Names<AggregateFunction>()}; Count needs no field, DistinctCount any field, the others a number field.");
        text.AppendLine($"bucket: {Names<DateBucket>()}; only date fields take a bucket other than None. sortBy: {Names<ReportSortBy>()}. top: 1..{ReportPlan.MaxTop}.");
        text.AppendLine("dateField: the date field the report's date range applies to. Charts, pivots need groupBy; KPIs have none and use the first aggregate.");
        text.AppendLine($"options: {{ chart: {Names<ChartKind>()}; format: {Names<NumberFormat>()}; currency (ISO code); showLegend; showDataLabels; compareToPrevious; higherIsBetter; sparkline (bucket of a KPI curve); target (number); view: {Names<TableView>()} }}");
        text.AppendLine("options.metrics: values a viewer can switch to: [{ label, aggregates: [...], format, filter }]. options.buckets: groupings to switch to: [{ label, field, bucket, dateField }].");
        text.AppendLine("options.thresholds: colour bands [{ value, color: success|warning|error|info|#rrggbb, label }], a value takes the band of the highest stop it reaches; options.colorBarsByThreshold for bar charts; options.thresholdColumn: the number column of a table.");
        text.AppendLine($"dateRange.preset: {Names<DateRangePreset>()} (Custom needs from and to, yyyy-MM-dd).");
        text.AppendLine("OData filters: field eq 'text', numbers plain, enums as Namespace.Type'Member' (the enum type is given below), dates as 2026-01-01T00:00:00Z (DateOnly fields 2026-01-01), and/or/not, contains(Field,'x'). Navigation fields use a slash: Customer/Name.");
        text.AppendLine();
        text.AppendLine("Data sets you may use (only these, only their fields, paths exactly as written):");
        foreach (var source in sources)
        {
            text.AppendLine($"- {source.EntitySet} \"{source.Label}\"{(source.Computed is null ? string.Empty : " (computed: no DataTable widget, no navigation fields)")}:");
            foreach (var field in source.Fields)
            {
                var dto = field.ToDto();
                var kind = dto.Kind switch
                {
                    ReportFieldKind.Enum => $"Enum {dto.EnumType}: {string.Join('|', dto.Members ?? [])}",
                    ReportFieldKind.Date when dto.DateOnly => "Date (DateOnly)",
                    var other => other.ToString(),
                };
                text.AppendLine($"  {dto.Path} \"{dto.Label}\" {kind}");
            }
        }

        text.AppendLine();
        text.AppendLine("Example:");
        text.AppendLine(JsonSerializer.Serialize(Example(sources), Json));
        return text.ToString();
    }

    public static string Input(string prompt, ReportDefinition? current) =>
        current is null
            ? $"Build this report: {prompt}"
            : $"Change this report as asked and answer with the whole changed definition. Keep widget ids and layout that need no change.\nAsked: {prompt}\nCurrent definition:\n{JsonSerializer.Serialize(current, Json)}";

    public static string Retry(string input, string answer, IEnumerable<string> errors) =>
        $"{input}\n\nYour last answer:\n{answer}\n\nIt cannot be used: {string.Join("; ", errors)}\nAnswer again with the whole corrected JSON object only.";

    private static string Names<T>() where T : struct, Enum => string.Join('|', Enum.GetNames<T>());

    // the first set with a category and a number gives the example real field names
    private static ReportDefinition Example(IReadOnlyList<ReportSource> sources)
    {
        var source = sources.FirstOrDefault(s => s.Fields.Any(f => f.IsNumeric)) ?? sources.FirstOrDefault();
        var category = source?.Fields.FirstOrDefault(f => f.Kind is ReportFieldKind.Enum or ReportFieldKind.Text)?.Path ?? "Status";
        var number = source?.Fields.FirstOrDefault(f => f.IsNumeric)?.Path ?? "Amount";
        var date = source?.Fields.FirstOrDefault(f => f.Kind == ReportFieldKind.Date)?.Path;
        var set = source?.EntitySet ?? "Orders";
        return new ReportDefinition
        {
            DateRange = date is null ? null : new ReportDateRange(DateRangePreset.Last12Months),
            Widgets =
            [
                new ReportWidget { Id = "total", Type = WidgetType.Kpi, Title = "Total", X = 0, Y = 0, W = 3, H = 3,
                    Query = new WidgetQuery { EntitySet = set, Aggregates = [new(AggregateFunction.Sum, number)], DateField = date },
                    Options = new WidgetOptions { Format = NumberFormat.Compact, Target = 1000, Thresholds = ReportThresholds.FromTarget(1000, true) } },
                new ReportWidget { Id = "split", Type = WidgetType.Chart, Title = "By category", X = 3, Y = 0, W = 6, H = 5,
                    Query = new WidgetQuery { EntitySet = set, GroupBy = new ReportGroupBy(category), Aggregates = [new()], DateField = date, SortBy = ReportSortBy.Value, Descending = true, Top = 10 },
                    Options = new WidgetOptions { Chart = ChartKind.Bar, Metrics = [new MetricView("Sum", [new(AggregateFunction.Sum, number)])] } },
            ],
        };
    }
}
