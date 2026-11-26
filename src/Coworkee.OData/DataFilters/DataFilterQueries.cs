using Coworkee.Contracts.Query;
using Coworkee.OData.Query;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OData;
using Microsoft.OData.Edm;

namespace Coworkee.OData.DataFilters;

/// <summary>Does a stored filter still read against the model of its set? Used when saving (refuse) and when listing (report).</summary>
internal static class DataFilterQueries
{
    /// <returns>What is wrong with the filter, or <c>null</c> when it parses.</returns>
    public static string? Problem(IServiceProvider services, ODataEntityRegistration entity, string queryJson)
    {
        if (QueryJson.Deserialize(queryJson) is not { } query)
        {
            return "The filter is not a query.";
        }

        var fields = EdmQueryFields.For(services.GetRequiredService<IEdmModel>(), entity.EntitySet);
        var known = fields.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (query.Rules().Select(r => r.Field).FirstOrDefault(f => !string.IsNullOrEmpty(f) && !known.Contains(f)) is { } unknown)
        {
            return $"The filter names the unknown property '{unknown}' of {entity.EntitySet}.";
        }

        if (ODataQueryTranslator.ToFilter(query, fields) is not { } filter)
        {
            return null;
        }

        try
        {
            _ = ODataQueryGuard.Options(services, entity.EntityType, QueryString.Create("$filter", filter)).Filter?.FilterClause;
            return null;
        }
        catch (Exception exception) when (exception is ODataException or InvalidOperationException)
        {
            return exception.Message;
        }
    }
}
