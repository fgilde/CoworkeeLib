using Coworkee.Contracts.Query;
using Coworkee.OData.Query;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OData;
using Microsoft.OData.Edm;

namespace Coworkee.OData.DataFilters;

/// <summary>Reads a stored filter against the model of its set: when saving (refuse), when listing (report) and when applying it (<see cref="DataFilterResolver"/>).</summary>
internal static class DataFilterQueries
{
    /// <returns>What is wrong with the filter, or <c>null</c> when it parses.</returns>
    public static string? Problem(IServiceProvider services, ODataEntityRegistration entity, string queryJson) => Parse(services, entity, queryJson).Problem;

    /// <summary>The parsed <c>$filter</c> of a stored query, as of <paramref name="now"/> for relative dates.</summary>
    /// <returns>The filter (<c>null</c> when the query has no complete rule: it lets every row pass), or what is wrong with it.</returns>
    public static (FilterQueryOption? Filter, string? Problem) Parse(IServiceProvider services, ODataEntityRegistration entity, string queryJson, DateTimeOffset? now = null)
    {
        if (QueryJson.Deserialize(queryJson) is not { } query)
        {
            return (null, "The filter is not a query.");
        }

        var fields = EdmQueryFields.For(services.GetRequiredService<IEdmModel>(), entity.EntitySet);
        var known = fields.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (query.Rules().Select(r => r.Field).FirstOrDefault(f => !string.IsNullOrEmpty(f) && !known.Contains(f)) is { } unknown)
        {
            return (null, $"The filter names the unknown property '{unknown}' of {entity.EntitySet}.");
        }

        if (ODataQueryTranslator.ToFilter(query, fields, now) is not { } filter)
        {
            return (null, null);
        }

        try
        {
            var option = ODataQueryGuard.Options(services, entity.EntityType, QueryString.Create("$filter", filter)).Filter;
            _ = option?.FilterClause;
            return (option, null);
        }
        catch (Exception exception) when (exception is ODataException or InvalidOperationException)
        {
            return (null, exception.Message);
        }
    }
}
