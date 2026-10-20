using System.Globalization;
using Coworkee.Contracts.Reporting;
using Coworkee.Reporting.Querying;

namespace Coworkee.Reporting.Subscriptions;

/// <summary>One widget of a subscription mail: a KPI value with its band, or the first rows of a chart, pivot or table; all rows for the workbook.</summary>
internal sealed class ReportMailWidget(
    IServiceProvider services, ReportQueries queries, IReadOnlyList<ReportSource> sources, ReportDefinition definition, ReportWidget widget, DateOnly today, CultureInfo culture,
    Func<string, CultureInfo, string> text)
{
    private const string Ink = "#16140f";

    private string Title => widget.Title is { Length: > 0 } title ? text(title, culture) : text(Querying.ReportSchema.Humanize(widget.Type.ToString()), culture);

    public async Task<(object Model, ReportSheetRequest? Sheet)> BuildAsync(CancellationToken cancellationToken)
    {
        var query = widget.Query!;
        if (sources.FirstOrDefault(s => string.Equals(s.EntitySet, query.EntitySet, StringComparison.OrdinalIgnoreCase)) is not { } source)
        {
            return (Failed(text("You may not read this data.", culture)), null);
        }

        var filter = ReportFilters.For(query, definition, [.. source.Fields.Select(f => f.ToDto())], today);
        try
        {
            return widget.Type switch
            {
                WidgetType.Kpi => await KpiAsync(query, filter, source, cancellationToken),
                WidgetType.Table or WidgetType.DataTable => await TableAsync(query, filter, source, cancellationToken),
                _ => await GroupsAsync(query, filter, source, cancellationToken),
            };
        }
        catch (Microsoft.OData.ODataException exception)
        {
            return (Failed(exception.Message), null);
        }
    }

    private async Task<(object, ReportSheetRequest?)> KpiAsync(WidgetQuery query, string? filter, ReportSource source, CancellationToken cancellationToken)
    {
        var aggregate = query.Aggregates.FirstOrDefault() ?? new ReportAggregate();
        var result = await queries.RunAsync(new ReportQueryRequest(query.EntitySet, filter, Aggregates: [aggregate]), cancellationToken);
        if (!result.IsSuccess)
        {
            return (Failed(result.Error.Message), null);
        }

        var value = result.Value.Rows.FirstOrDefault()?.Values.FirstOrDefault();
        var band = ReportThresholds.Find(widget.Options.Thresholds, value);
        var model = new
        {
            title = Title,
            kpi = true,
            value = ReportNumbers.Format(value, widget.Options, culture),
            color = band is null ? Ink : ReportThresholds.Hex(band.Color),
            symbol = band is null ? string.Empty : ReportThresholds.Symbol(band.Color),
            band = band?.Label is { Length: > 0 } label ? text(label, culture) : string.Empty,
            headers = Array.Empty<string>(),
            rows = Array.Empty<string[]>(),
            more = false,
            error = string.Empty,
        };
        return (model, Sheet([Label(aggregate, source)], [[value]]));
    }

    private async Task<(object, ReportSheetRequest?)> GroupsAsync(WidgetQuery query, string? filter, ReportSource source, CancellationToken cancellationToken)
    {
        if (query.GroupBy is null)
        {
            return (Failed(text("Choose what to group by.", culture)), null);
        }

        var result = await queries.RunAsync(ReportQueryRequest.From(query, filter), cancellationToken);
        if (!result.IsSuccess)
        {
            return (Failed(result.Error.Message), null);
        }

        var split = query.SplitBy is not null;
        var values = split ? [query.Aggregates.FirstOrDefault() ?? new ReportAggregate()] : query.Aggregates;
        List<string> headers = [FieldLabel(source, query.GroupBy.Field), .. split ? [FieldLabel(source, query.SplitBy!.Field)] : Array.Empty<string>(), .. values.Select(a => Label(a, source))];
        // a time series shows its latest buckets, oldest first (keys are ISO dates)
        var series = query.GroupBy.Bucket != DateBucket.None;
        var ordered = series ? result.Value.Rows.OrderBy(r => r.Key?.Value, StringComparer.Ordinal) : result.Value.Rows.AsEnumerable();
        var rows = ordered.Select(r => (IReadOnlyList<object?>)[Key(r.Key), .. split ? [Key(r.Series)] : Array.Empty<object?>(), .. r.Values.Take(values.Count).Cast<object?>()]).ToList();
        return (Table(headers, rows, cell => cell is double number ? ReportNumbers.Format(number, widget.Options, culture) : cell?.ToString() ?? string.Empty, latest: series), Sheet(headers, rows));
    }

    private async Task<(object, ReportSheetRequest?)> TableAsync(WidgetQuery query, string? filter, ReportSource source, CancellationToken cancellationToken)
    {
        List<ReportField> columns = query.Columns.Count > 0
            ? [.. query.Columns.Select(source.Find).OfType<ReportField>()]
            : [.. source.Fields.Where(f => f.Kind != ReportFieldKind.Guid && !f.Path.Contains('/')).Take(6)];
        var rows = await ReportTableRows.ReadAsync(services, source, columns, filter, source.Find(query.OrderBy), query.Descending, ReportSubscriptionMail.SheetRows, cancellationToken);
        List<string> headers = [.. columns.Select(c => text(c.Label, culture))];
        var cells = rows.Select(r => (IReadOnlyList<object?>)[.. r.Select(Cell)]).ToList();
        return (Table(headers, cells, Format), Sheet(headers, cells));
    }

    private object Table(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<object?>> rows, Func<object?, string> format, bool latest = false) => new
    {
        title = Title,
        kpi = false,
        value = string.Empty,
        color = Ink,
        symbol = string.Empty,
        band = string.Empty,
        headers,
        rows = (latest ? rows.TakeLast(ReportSubscriptionMail.MailRows) : rows.Take(ReportSubscriptionMail.MailRows)).Select(r => r.Select(format).ToArray()).ToArray(),
        more = rows.Count > ReportSubscriptionMail.MailRows,
        latest,
        error = string.Empty,
    };

    private object Failed(string error) => new
    {
        title = Title, kpi = false, value = string.Empty, color = Ink, symbol = string.Empty, band = string.Empty,
        headers = Array.Empty<string>(), rows = Array.Empty<string[]>(), more = false, error,
    };

    private ReportSheetRequest Sheet(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows) =>
        new(Title, headers, [.. rows.Select(r => (IReadOnlyList<System.Text.Json.JsonElement>)[.. r.Select(c => System.Text.Json.JsonSerializer.SerializeToElement(c, ReportHandlers.Json))])]);

    private string Label(ReportAggregate aggregate, ReportSource source) =>
        aggregate.Label is { Length: > 0 } label ? text(label, culture)
        : aggregate.Function == AggregateFunction.Count ? text("Count", culture)
        : $"{text(Querying.ReportSchema.Humanize(aggregate.Function.ToString()), culture)} {FieldLabel(source, aggregate.Field)}";

    private string FieldLabel(ReportSource source, string? path) => source.Find(path) is { } field ? text(field.Label, culture) : path ?? string.Empty;

    private string Key(ReportKey? key) => key is null ? string.Empty : key.Value is null ? text("(empty)", culture) : text(key.Label, culture);

    // enums by their label, dates as days, numbers stay numbers for the workbook
    private object? Cell(object? value) => value switch
    {
        Enum member => text(Contracts.Data.EnumLabels.Of(member), culture),
        DateTimeOffset at => at.ToString("g", culture),
        DateTime at => at.ToString("g", culture),
        DateOnly day => day.ToString("d", culture),
        bool flag => text(flag ? "Yes" : "No", culture),
        decimal or double or float or int or long or short or byte => Convert.ToDouble(value, CultureInfo.InvariantCulture),
        null => null,
        _ => value.ToString(),
    };

    private string Format(object? cell) => cell is double number ? number.ToString(number == Math.Round(number) ? "N0" : "N2", culture) : cell?.ToString() ?? string.Empty;
}
