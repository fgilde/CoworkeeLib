using Microsoft.AspNetCore.OData.Query;

namespace Coworkee.Reporting.Sources;

internal sealed class ReportSourceRegistration<TRow>(string name, string label, string? permission, Func<ReportSourceContext, CancellationToken, Task<IEnumerable<TRow>>> rows)
    : ReportSourceRegistration(name, label, permission, typeof(TRow))
    where TRow : class
{
    public override async Task<IQueryable> RowsAsync(IServiceProvider services, string? filter, CancellationToken cancellationToken)
    {
        var option = Option(filter, services);
        var (from, to) = ReportSourceDates.Bounds(option?.FilterClause);
        var result = await rows(new ReportSourceContext(services, from, to), cancellationToken);
        // ponytail: a list is filtered and aggregated in memory; return an IQueryable of the database when the rows get many
        var query = result as IQueryable<TRow> ?? result.AsQueryable();
        return option is null ? query : option.ApplyTo(query, new ODataQuerySettings());
    }
}
