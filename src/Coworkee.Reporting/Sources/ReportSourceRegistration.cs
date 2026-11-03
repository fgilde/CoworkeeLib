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

    /// <summary>Parses the filter; unknown fields, bad syntax or a filter beyond the query limits throw an ODataException.</summary>
    public FilterClause? Parse(string? filter, IServiceProvider services) => Option(filter, services)?.FilterClause;

    public abstract Task<IQueryable> RowsAsync(IServiceProvider services, string? filter, CancellationToken cancellationToken);

    protected FilterQueryOption? Option(string? filter, IServiceProvider services)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return null;
        }

        // every field of the row type may be filtered; the query limits still apply
        var context = new ODataQueryContext(Model, rowType, new ODataPath());
        context.DefaultQueryConfigurations.EnableFilter = true;
        var option = new FilterQueryOption(filter, context, new ODataQueryOptionParser(Model, EdmType, null, new Dictionary<string, string> { ["$filter"] = filter }));
        option.Validate(Coworkee.OData.ODataLimits.Settings(services));
        return option;
    }

    private IEdmModel BuildModel()
    {
        var builder = new ODataConventionModelBuilder();
        builder.AddComplexType(rowType);
        return builder.GetEdmModel();
    }
}
