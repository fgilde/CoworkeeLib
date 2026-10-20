using Microsoft.AspNetCore.OData.Query;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using Microsoft.OData.UriParser;

namespace Coworkee.Reporting.Sources;

/// <summary>A data source an app computes: rows of a plain type, filtered by OData against a model of just that type.</summary>
internal abstract class ReportSourceRegistration(string name, string label, string? permission, Type rowType)
{
    private IEdmModel? _model;

    public string Name => name;

    public string Label => label;

    public string? Permission => permission;

    public Type RowType => rowType;

    public IEdmModel Model => _model ??= BuildModel();

    public IEdmStructuredType EdmType => (IEdmStructuredType)Model.FindDeclaredType(rowType.FullName!)!;

    /// <summary>Parses the filter; unknown fields or bad syntax throw an ODataException.</summary>
    public FilterClause? Parse(string? filter) => Option(filter)?.FilterClause;

    public abstract Task<IQueryable> RowsAsync(IServiceProvider services, string? filter, CancellationToken cancellationToken);

    protected FilterQueryOption? Option(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return null;
        }

        var context = new ODataQueryContext(Model, rowType, new ODataPath());
        return new FilterQueryOption(filter, context, new ODataQueryOptionParser(Model, EdmType, null, new Dictionary<string, string> { ["$filter"] = filter }));
    }

    private IEdmModel BuildModel()
    {
        var builder = new ODataConventionModelBuilder();
        builder.AddComplexType(rowType);
        return builder.GetEdmModel();
    }
}

internal sealed class ReportSourceRegistration<TRow>(string name, string label, string? permission, Func<ReportSourceContext, CancellationToken, Task<IEnumerable<TRow>>> rows)
    : ReportSourceRegistration(name, label, permission, typeof(TRow))
    where TRow : class
{
    public override async Task<IQueryable> RowsAsync(IServiceProvider services, string? filter, CancellationToken cancellationToken)
    {
        var option = Option(filter);
        var (from, to) = ReportSourceDates.Bounds(option?.FilterClause);
        var result = await rows(new ReportSourceContext(services, from, to), cancellationToken);
        // ponytail: a list is filtered and aggregated in memory; return an IQueryable of the database when the rows get many
        var query = result as IQueryable<TRow> ?? result.AsQueryable();
        return option is null ? query : option.ApplyTo(query, new ODataQuerySettings());
    }
}
