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

    private async Task<Error?> WidgetAsync(ReportWidget widget, string key, IReadOnlyList<ReportSource> sources, CancellationToken cancellationToken)
    {
        var query = widget.Query!;
        if (await queries.CheckAsync(ReportQueryRequest.From(query, query.Filter), cancellationToken) is { } error)
        {
            return error.Kind == ErrorKind.Validation && error.Details is { } details
                ? Error.Validation(details.ToDictionary(d => $"{key}.{d.Key}", d => d.Value, StringComparer.Ordinal))
                : error with { Message = $"{widget.Title ?? widget.Id}: {error.Message}" };
        }

        var source = sources.FirstOrDefault(s => string.Equals(s.EntitySet, query.EntitySet, StringComparison.OrdinalIgnoreCase));
        return query.Columns.Concat(new[] { query.DateField, query.OrderBy, widget.Options.ThresholdColumn }.OfType<string>()).FirstOrDefault(c => source?.Find(c) is null) is { } column
            ? Error.Validation($"{key}.Columns", $"{query.EntitySet} has no field '{column}'.")
            : null;
    }
}
