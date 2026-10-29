using System.Collections;
using System.Reflection;
using Coworkee.Contracts.Data;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OData.Extensions;
using Microsoft.AspNetCore.OData.Query.Container;
using Microsoft.AspNetCore.OData.Query.Wrapper;
using Microsoft.OData;
using Microsoft.OData.Edm;
using Nextended.Web.OData;

namespace Coworkee.OData;

/// <summary>
/// An OData list read in process, e.g. for REST endpoints that answer from an entity set: the same steps as <c>GET /{route}/{set}?{query}</c> for the
/// current user (permission, hidden and protected properties, row filters, navigation scope, query checks, search, facets), without HTTP.
/// </summary>
public interface IODataQueries
{
    /// <param name="route">The route prefix whose names the query uses, e.g. <c>odata</c> or <c>rt/odata</c>.</param>
    /// <param name="query">The query string, e.g. <c>$filter=Price gt 5&amp;$orderby=Name&amp;$top=20&amp;$count=true</c>.</param>
    /// <param name="facets">Builds the facets of the page, as <c>Prefer: odata.include-annotations="cn.facets"</c> does.</param>
    /// <returns>The page; Forbidden or Validation errors where the route answers 403 or 400.</returns>
    Task<Result<ODataQueryResult<T>>> QueryAsync<T>(string route, string entitySet, string? query, bool facets = false, CancellationToken cancellationToken = default)
        where T : class;
}

/// <param name="Rows">
/// The entities, when the query does not shape them; what the route would not write (hidden or protected properties, members outside the model) is
/// left at its default.
/// </param>
/// <param name="Shaped">
/// The rows as the route writes them, by the route's property names, when <c>$select</c>, <c>$expand</c>, <c>$apply</c> or an [AutoExpand] navigation shapes them;
/// expanded navigations are nested dictionaries or lists of them. Null otherwise.
/// </param>
/// <param name="Count">The number of matching rows with <c>$count=true</c>.</param>
/// <param name="Facets">The facets in the route's names, when asked for.</param>
public sealed record ODataQueryResult<T>(
    IReadOnlyList<T> Rows, IReadOnlyList<IReadOnlyDictionary<string, object?>>? Shaped, long? Count, IReadOnlyList<FacetGroupDto>? Facets);

internal sealed class ODataQueries(IServiceProvider services, ODataRoutes routes, ODataEntityRegistry registry, ICurrentUser user, IHttpContextAccessor? accessor = null)
    : IODataQueries
{
    public async Task<Result<ODataQueryResult<T>>> QueryAsync<T>(string route, string entitySet, string? query, bool facets, CancellationToken cancellationToken)
        where T : class
    {
        var profile = routes.Find(route) ?? throw new ArgumentException($"There is no OData route {route}.", nameof(route));
        if (registry.Find(entitySet) is not { } set || set.EntityType != typeof(T))
        {
            throw new ArgumentException($"{entitySet} is no OData set of {typeof(T).Name}.", nameof(entitySet));
        }

        // [Authorize] and the set's permission, as the controller has them
        if (!user.IsAuthenticated)
        {
            return Error.Unauthorized("odata.unauthorized", "Sign in to read OData sets.");
        }

        if (!await ODataEntityAccess.IsGrantedAsync(services, typeof(T), cancellationToken))
        {
            return Error.Forbidden("odata.forbidden", $"You may not read {set.EntitySet}.");
        }

        var http = Request(profile, set.EntitySet, query, facets, cancellationToken);
        if (await ODataPipeline.PrepareAsync<T>(http, byKey: false) is { } refusal)
        {
            return refusal.Status == StatusCodes.Status400BadRequest
                ? Error.Validation(nameof(query), refusal.Detail ?? "The query is not valid.")
                : Error.Forbidden("odata.forbidden", refusal.Detail ?? "The query reads a property you may not read.");
        }

        try
        {
            var rows = await ODataPipeline.RowsAsync<T>(http, profile, cancellationToken);
            var model = ODataPipeline.Model(http.Request);

            // what [EnableQuery] does on the route: validate with its defaults, then apply in the scope's two steps
            var options = http.Request.ODataQueryOptions<T>(model, model.EntityContainer.FindEntitySet(set.EntitySet));
            var enableQuery = new ScopedEnableQueryAttribute();
            enableQuery.ValidateQuery(http.Request, options);
            var items = new List<object>();
            await foreach (var item in ((IAsyncEnumerable<object>)enableQuery.ApplyQuery(rows, options)).WithCancellation(cancellationToken))
            {
                items.Add(item);
            }

            var feature = http.ODataFeature();
            var count = feature.TotalCount ?? feature.TotalCountFunc?.Invoke();
            var denied = (Dictionary<string, IReadOnlySet<string>>)http.Items[ODataEntityAccess.DeniedItem]!;
            var groups = http.Items[FacetResourceSetSerializer.FacetsAnnotationName] as List<FacetGroupDto>;
            // an empty page is shaped when the options would shape its rows
            if (items.Count > 0 ? items.All(i => i is T) : options.SelectExpand is null && options.Apply is null && options.Compute is null)
            {
                return new ODataQueryResult<T>([.. items.Cast<T>().Select(row => Blank(row, model, denied))], null, count, groups);
            }

            return new ODataQueryResult<T>([], [.. items.Select(i => (IReadOnlyDictionary<string, object?>)Shape(i, denied)!)], count, groups);
        }
        catch (ODataException exception)
        {
            return Error.Validation(nameof(query), exception.Message);
        }
    }

    // a request of the route as the controller sees it, with the services of the current scope (user, tenant, group selection)
    private DefaultHttpContext Request(ODataRouteProfile profile, string entitySet, string? query, bool facets, CancellationToken cancellationToken)
    {
        var http = new DefaultHttpContext { RequestServices = services, RequestAborted = cancellationToken };
        if (accessor?.HttpContext?.User is { } principal)
        {
            http.User = principal;
        }

        http.Request.Method = HttpMethods.Get;
        http.Request.Path = $"/{profile.Prefix}/{entitySet}";
        http.Request.QueryString = string.IsNullOrWhiteSpace(query) ? QueryString.Empty : new QueryString("?" + query.TrimStart('?'));
        if (facets)
        {
            http.Request.Headers["Prefer"] = $"odata.include-annotations=\"{FacetResourceSetSerializer.FacetsAnnotationName}\"";
        }

        http.ODataFeature().RoutePrefix = profile.Prefix;
        return http;
    }

    // the serializer writes what the row's EDM type has and the user may read; an entity handed to app code has everything else at its default
    // (hidden, protected, never in the model); a getter without a field computes from the blanked members
    private static T Blank<T>(T row, IEdmModel model, Dictionary<string, IReadOnlySet<string>> denied)
        where T : class
    {
        var type = row.GetType();
        var edm = model.FindDeclaredType(type.FullName) as IEdmStructuredType;
        var shown = new HashSet<string>(edm?.Properties().Select(p => ODataNames.Clr(model, p)) ?? [], StringComparer.OrdinalIgnoreCase);
        for (var t = edm; t is not null; t = t.BaseType())
        {
            shown.ExceptWith(denied.GetValueOrDefault(t.FullTypeName()) ?? new HashSet<string>());
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0 && !shown.Contains(p.Name)))
        {
            var value = property.PropertyType.IsValueType ? Activator.CreateInstance(property.PropertyType) : null;
            if (property.SetMethod is not null)
            {
                property.SetValue(row, value);
            }
            else
            {
                Backing(property)?.SetValue(row, value);
            }
        }

        return row;
    }

    private static FieldInfo? Backing(PropertyInfo property) =>
        property.DeclaringType!.GetField($"<{property.Name}>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);

    // a shaped row as a dictionary by the route's names, without the members the user may not read (by its own type and bases, like the serializer)
    private static object? Shape(object? value, Dictionary<string, IReadOnlySet<string>> denied)
    {
        switch (value)
        {
            case ISelectExpandWrapper wrapper:
                IEdmModel? model = null;
                IEdmStructuredType? type = null;
                var values = wrapper.ToDictionary((m, t) =>
                {
                    (model, type) = (m, t);
                    return Names.Instance;
                });
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (var t = type; t is not null; t = t.BaseType())
                {
                    names.UnionWith(denied.GetValueOrDefault(t.FullTypeName()) ?? new HashSet<string>());
                }

                return values
                    .Where(v => type?.FindProperty(v.Key) is not { } property || !names.Contains(ODataNames.Clr(model!, property)))
                    .ToDictionary(v => v.Key, v => Shape(v.Value, denied));
            case DynamicTypeWrapper group:
                return group.Values.ToDictionary(v => v.Key, v => Shape(v.Value, denied));
            case IEnumerable items when value is not string and not byte[]:
                return items.Cast<object?>().Select(i => Shape(i, denied)).ToList();
            default:
                return value;
        }
    }

    private sealed class Names : IPropertyMapper
    {
        public static readonly Names Instance = new();

        public string MapProperty(string propertyName) => propertyName;
    }
}
