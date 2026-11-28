using System.Globalization;
using Coworkee.Contracts.Reporting;
using Coworkee.Reporting.Querying;

namespace Coworkee.Reporting.Subscriptions;

/// <summary>One widget of a subscription mail: a KPI value with its band, or the first rows of a chart, pivot or table (a grouped table its first page); all rows for the workbook.</summary>
internal sealed class ReportMailWidget(
    IServiceProvider services, ReportQueries queries, IReadOnlyList<ReportSource> sources, ReportDefinition definition, ReportWidget widget, DateOnly today, CultureInfo culture,
    Func<string, CultureInfo, string> text)
{
    private const string Ink = "#16140f";

    private bool _translated;

    private string Title => widget.Title is { Length: > 0 } title ? (_translated ? title : text(title, culture)) : text(Querying.ReportSchema.Humanize(widget.Type.ToString()), culture);

    public async Task<(object Model, ReportSheetRequest? Sheet)> BuildAsync(CancellationToken cancellationToken)
    {
        var query = widget.Query!;
        if (sources.FirstOrDefault(s => string.Equals(s.EntitySet, query.EntitySet, StringComparison.OrdinalIgnoreCase)) is not { } source)
        {
            return (Failed(text("You may not read this data.", culture)), null);
        }

        widget = ReportViews.WithSourceCurrency(widget, await queries.CurrencyAsync(source, cancellationToken), t => text(t, culture));
        _translated = true;
        var filter = FilterOf(query, definition, sources, today);
        try
        {
            return widget.Type switch
            {
                WidgetType.Kpi => await KpiAsync(query, filter, source, cancellationToken),
                WidgetType.Table or WidgetType.DataTable => await TableAsync(query, filter, source, cancellationToken),
                WidgetType.GroupedTable => await GroupedTableAsync(source, cancellationToken),
                _ => await GroupsAsync(query, filter, source, cancellationToken),
            };
        }
        catch (Microsoft.OData.ODataException exception)
        {
            return (Failed(exception.Message), null);
        }
    }

    /// <summary>The widget's rows: its own filter and the report's facets, also those of other sets with the same field.</summary>
    internal static string? FilterOf(WidgetQuery query, ReportDefinition definition, IReadOnlyList<ReportSource> sources, DateOnly today)
    {
        IReadOnlyList<ReportFieldDto>? FieldsOf(string set) => sources.FirstOrDefault(s => string.Equals(s.EntitySet, set, StringComparison.OrdinalIgnoreCase)) is { } found ? [.. found.Fields.Select(f => f.ToDto())] : null;
        return ReportFilters.For(query, definition, FieldsOf(query.EntitySet) ?? [], today, FieldsOf);
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

        if (widget.Options.Chart == ChartKind.Chips && widget.Type == WidgetType.Chart)
        {
            return Chips(query, source, result.Value);
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

    // chips as a list of shares of everything the query returned, for the values with an icon in their order
    private (object, ReportSheetRequest?) Chips(WidgetQuery query, ReportSource source, ReportQueryResult result)
    {
        List<string> headers = [FieldLabel(source, query.GroupBy!.Field), text("Share", culture)];
        var rows = ChipShares(result.Rows, widget.Options.ValueIcons).Select(s => (IReadOnlyList<object?>)[Key(s.Key), s.Share]).ToList();
        return (Table(headers, rows, cell => cell is double share ? $"{share:0} %" : cell?.ToString() ?? string.Empty), Sheet(headers, rows));
    }

    /// <summary>
    /// The share in percent of each value with an icon, in the icons' order, as the chips widget shows it: rows without a key are left out of the whole,
    /// a row whose key has no value counts for the icon with an empty value ("(empty)").
    /// </summary>
    internal static List<(ReportKey Key, double Share)> ChipShares(IEnumerable<ReportQueryRow> rows, IEnumerable<ValueIcon> icons)
    {
        // a stored definition may still split into series: every value adds up over all of them
        var sums = rows.Where(r => r.Key is not null).GroupBy(r => r.Key!.Value ?? string.Empty)
            .ToDictionary(g => g.Key, g => (Key: g.First().Key!, Sum: g.Sum(r => r.Values.FirstOrDefault() ?? 0)));
        var total = sums.Values.Sum(v => v.Sum);
        return [.. icons.Where(icon => sums.ContainsKey(icon.Value)).Select(icon => sums[icon.Value])
            .Select(s => (s.Key, total == 0 ? 0 : Math.Round(s.Sum / total * 100, MidpointRounding.AwayFromZero)))];
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

    // the first page as the widget shows it, its rows up to the workbook's limit; joins on sets the subscriber may not read were left out (ReportViews.ForReader)
    private async Task<(object, ReportSheetRequest?)> GroupedTableAsync(ReportSource source, CancellationToken cancellationToken)
    {
        var query = widget.Query!;
        var columns = ReportTableColumns.Of(query);
        var request = ReportTableColumns.Request(query, q => FilterOf(q, definition, sources, today), ReportTableColumns.Sorts(query.OrderBy, columns), 0, ReportSubscriptionMail.SheetRows);
        var result = await queries.TableAsync(request, cancellationToken);
        if (!result.IsSuccess)
        {
            return (Failed(result.Error.Message), null);
        }

        var shown = columns.Where(c => !ReportTableColumns.Hidden(c, widget.Options)).ToList();
        var keys = query.GroupFields.Count;

        // a currency value shows the currency of its own set; the widget's (already its source's) for its own values
        var currencies = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var join in query.Joins.Where(j => !currencies.ContainsKey(j.EntitySet)))
        {
            currencies[join.EntitySet] = Source(join.EntitySet) is { } joined ? await queries.CurrencyAsync(joined, cancellationToken) : null;
        }

        List<string> headers = [.. shown.Select(c => c.Aggregate is { } aggregate ? Header(c, aggregate, source) : FieldLabel(source, c.Field))];
        var rows = result.Value.Rows.Select(r => (IReadOnlyList<object?>)[.. shown.Select(c => c.Index < keys ? Key(r.Keys[c.Index]) : (object?)r.Values[c.Index - keys])]).ToList();
        var cells = rows.Select(r => (IReadOnlyList<object?>)[.. r.Select((cell, i) => shown[i].Aggregate is { } aggregate
            ? ReportTableColumns.Format((double?)cell, aggregate, widget.Options, culture, shown[i].Join is null ? null : currencies.GetValueOrDefault(shown[i].EntitySet), t => text(t, culture))
            : cell)]).ToList();
        return (Table(headers, cells, cell => cell?.ToString() ?? string.Empty, shown: ReportTableColumns.PageSize, more: result.Value.More), Sheet(headers, rows));
    }

    // a join's value without a label is named with its set, like the column (ReportTableColumns.Name)
    private string Header(ReportTableColumns.Column column, ReportAggregate aggregate, ReportSource main) =>
        column.Join is null || aggregate.Label is { Length: > 0 } ? Label(aggregate, Source(column.EntitySet) ?? main)
        : $"{Label(aggregate, Source(column.EntitySet) ?? main)} ({text(Source(column.EntitySet)?.Label ?? column.EntitySet, culture)})";

    private ReportSource? Source(string set) => sources.FirstOrDefault(s => string.Equals(s.EntitySet, set, StringComparison.OrdinalIgnoreCase));

    private object Table(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<object?>> rows, Func<object?, string> format, bool latest = false, int shown = ReportSubscriptionMail.MailRows, bool more = false) => new
    {
        title = Title,
        kpi = false,
        value = string.Empty,
        color = Ink,
        symbol = string.Empty,
        band = string.Empty,
        headers,
        rows = (latest ? rows.TakeLast(shown) : rows.Take(shown)).Select(r => r.Select(format).ToArray()).ToArray(),
        more = more || rows.Count > shown,
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
