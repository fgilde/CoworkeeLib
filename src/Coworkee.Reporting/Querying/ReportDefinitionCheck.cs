using Coworkee.Contracts.Reporting;
using Coworkee.Core.Results;

namespace Coworkee.Reporting.Querying;

/// <summary>Whether every widget of a definition works for the current user: the widget as designed and each alternative a viewer can switch it to.</summary>
internal sealed class ReportDefinitionCheck(ReportQueries queries)
{
    public async Task<IReadOnlyList<Error>> WidgetsAsync(ReportDefinition definition, bool stopAtFirst, CancellationToken cancellationToken)
    {
        var errors = new List<Error>();
        var sources = await queries.SourcesAsync(cancellationToken);
        errors.AddRange(Fields(definition, sources));
        if (stopAtFirst && errors.Count > 0)
        {
            return errors;
        }

        foreach (var widget in definition.Widgets.Where(w => w.Query is not null && w.Type is not (WidgetType.Markdown or WidgetType.Image or WidgetType.Embed)))
        {
            foreach (var (view, shown) in ReportViews.All(widget))
            {
                if (await WidgetAsync(shown, view.Length == 0 ? $"Widgets[{widget.Id}]" : $"Widgets[{widget.Id}].{view}", sources, cancellationToken) is { } error)
                {
                    errors.Add(error);
                    if (stopAtFirst)
                    {
                        return errors;
                    }
                }
            }
        }

        return errors;
    }

    // the choosable date fields and the filter bar's fields must exist in a data set of the report engine
    private static IEnumerable<Error> Fields(ReportDefinition definition, IReadOnlyList<ReportSource> sources)
    {
        foreach (var path in definition.DateFields.Where(d => !sources.Any(s => s.Find(d) is { Kind: ReportFieldKind.Date })))
        {
            yield return Error.Validation("Definition.DateFields", $"'{path}' is not a date field of any data set.");
        }

        foreach (var path in definition.FacetFields.Where(f => !sources.Any(s => s.Find(f) is not null)))
        {
            yield return Error.Validation("Definition.FacetFields", $"'{path}' is not a field of any data set.");
        }
    }

    private async Task<Error?> WidgetAsync(ReportWidget widget, string key, IReadOnlyList<ReportSource> sources, CancellationToken cancellationToken)
    {
        var query = widget.Query!;
        var grouped = widget.Type == WidgetType.GroupedTable;

        // a grouped table's columns are named (order, hidden columns): two of the same name would be one
        if (grouped && ReportTableColumns.Clashes(ReportTableColumns.Of(query)) is [var clash, ..])
        {
            return Error.Validation($"{key}.{clash.Key}{(clash.Aggregate is null ? string.Empty : ".Label")}", $"Another column is named '{clash.Name}'; give this one another label.");
        }

        var check = grouped
            ? queries.CheckTableAsync(ReportTableColumns.Request(query, q => q.Filter, null, 0, 1), cancellationToken)
            : queries.CheckAsync(ReportQueryRequest.From(query, query.Filter), cancellationToken);
        if (await check is { } error)
        {
            return error.Kind == ErrorKind.Validation && error.Details is { } details
                ? Error.Validation(details.ToDictionary(d => $"{key}.{d.Key}", d => d.Value, StringComparer.Ordinal))
                : error with { Message = $"{widget.Title ?? widget.Id}: {error.Message}" };
        }

        var source = sources.FirstOrDefault(s => string.Equals(s.EntitySet, query.EntitySet, StringComparison.OrdinalIgnoreCase));
        // a grouped table's order names columns (missing ones are skipped), not fields
        return query.Columns.Concat(new[] { query.DateField, grouped ? null : query.OrderBy, widget.Options.ThresholdColumn }.OfType<string>()).FirstOrDefault(c => source?.Find(c) is null) is { } column
            ? Error.Validation($"{key}.Columns", $"{query.EntitySet} has no field '{column}'.")
            : null;
    }
}
