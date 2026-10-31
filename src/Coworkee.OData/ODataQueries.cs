using System.Collections;
using System.Reflection;
using Coworkee.Contracts.Data;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OData.Extensions;
using Microsoft.AspNetCore.OData.Query.Container;
using Microsoft.AspNetCore.OData.Query.Wrapper;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;
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
        where T : class =>
        QueryAsync<T>(route, entitySet, query, facets, null, cancellationToken);

    /// <summary>
    /// The list read with the facet choice apart from the filter (<see cref="FacetContextDto"/>, the <c>Coworkee-Facets</c> header of the route): each facet
    /// group counts without its own choice, as the route counts it for a table. An endpoint that answers a table's query forwards the table's context.
    /// </summary>
    /// <param name="facetContext">The table's facet choice; null counts every group with the whole filter.</param>
    Task<Result<ODataQueryResult<T>>> QueryAsync<T>(string route, string entitySet, string? query, bool facets, FacetContextDto? facetContext,
        CancellationToken cancellationToken = default)
        where T : class;
}

/// <param name="Rows">
/// The entities, when the query does not shape them; what the route would not write (hidden or protected properties, members outside the model) is
/// cleared to its default. Empty when the rows are <paramref name="Shaped"/>.
/// </param>
/// <param name="Shaped">
/// The rows as the route writes them, by the route's property names, when <c>$select</c>, <c>$expand</c>, <c>$apply</c> or an [AutoExpand] navigation shapes them,
/// or when a member the user may not read cannot be cleared on the entity (a getter without a field EF knows): expanded navigations are nested
/// dictionaries or lists of them. Null otherwise.
/// </param>
/// <param name="Count">The number of matching rows with <c>$count=true</c>.</param>
/// <param name="Facets">The facets in the route's names, when asked for.</param>
public sealed record ODataQueryResult<T>(
    IReadOnlyList<T> Rows, IReadOnlyList<IReadOnlyDictionary<string, object?>>? Shaped, long? Count, IReadOnlyList<FacetGroupDto>? Facets);

internal sealed class ODataQueries(
    IServiceProvider services, ODataRoutes routes, ODataEntityRegistry registry, ICurrentUser user, ILogger<ODataQueries> logger,
    Microsoft.Extensions.Options.IOptions<CoworkeeODataOptions> limits, IHttpContextAccessor? accessor = null)
    : IODataQueries
{
    public async Task<Result<ODataQueryResult<T>>> QueryAsync<T>(string route, string entitySet, string? query, bool facets, FacetContextDto? facetContext,
        CancellationToken cancellationToken)
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

        var http = Request(profile, set.EntitySet, query, facets, facetContext, cancellationToken);
        if (await ODataPipeline.PrepareAsync<T>(http, byKey: false) is { } refusal)
        {
            return refusal.Status == StatusCodes.Status400BadRequest
                ? Error.Validation(nameof(query), refusal.Detail ?? "The query is not valid.")
                : Error.Forbidden("odata.forbidden", refusal.Detail ?? "The query reads a property you may not read.");
        }

        try
        {
            var model = ODataPipeline.Model(http.Request);
            var edmSet = model.EntityContainer.FindEntitySet(set.EntitySet);

            // what [EnableQuery] does on the route: validate with the configured limits (before the facets read the filter), page by the model's page
            // size, then apply in the scope's two steps
            var options = http.Request.ODataQueryOptions<T>(model, edmSet);
            var enableQuery = new ScopedEnableQueryAttribute(limits.Value);
            enableQuery.ValidateQuery(http.Request, options);
            var rows = await ODataPipeline.RowsAsync<T>(http, profile, cancellationToken);
            enableQuery.UseModelPageSize(model, edmSet.EntityType);
            var items = new List<object>();
            var applied = enableQuery.ApplyQuery(rows, options);
            if (applied is IAsyncEnumerable<object> pending)
            {
                await foreach (var item in pending.WithCancellation(cancellationToken))
                {
                    items.Add(item);
                }
            }
            else
            {
                // a page of the model's page size is read already (OData's truncated collection)
                items.AddRange(applied.Cast<object>());
            }

            var feature = http.ODataFeature();
            var count = feature.TotalCount ?? feature.TotalCountFunc?.Invoke();
            var denied = (Dictionary<string, IReadOnlySet<string>>)http.Items[ODataEntityAccess.DeniedItem]!;
            var groups = http.Items[FacetResourceSetSerializer.FacetsAnnotationName] as List<FacetGroupDto>;

            // an empty page is shaped when the options would shape its rows
            var typed = items.Count > 0 ? items.All(i => i is T) : options.SelectExpand is null && options.Apply is null && options.Compute is null;
            if (!typed)
            {
                return new ODataQueryResult<T>([], [.. items.Select(i => (IReadOnlyDictionary<string, object?>)Shape(i, denied)!)], count, groups);
            }

            var clearers = items.Select(i => i.GetType()).Distinct().ToDictionary(t => t, t => Clearer(t, model, denied));
            if (clearers.Values.Any(c => c is null))
            {
                // fails closed: no entity is handed out that keeps a member the user may not read
                return new ODataQueryResult<T>([], [.. items.Select(i => Written(i, model, denied))], count, groups);
            }

            foreach (var item in items)
            {
                clearers[item.GetType()]!(item);
            }

            return new ODataQueryResult<T>([.. items.Cast<T>()], null, count, groups);
        }
        catch (ODataException exception)
        {
            return Error.Validation(nameof(query), exception.Message);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // like the route: EF's messages can quote row filters, so they go to the log only
            logger.LogWarning(exception, "An in-process OData query on {Route}/{Set} failed.", profile.Prefix, set.EntitySet);
            return Error.Validation(nameof(query), "The query could not be run.");
        }
    }

    // a request of the route as the controller sees it, with the services of the current scope (user, tenant, group selection)
    private DefaultHttpContext Request(ODataRouteProfile profile, string entitySet, string? query, bool facets, FacetContextDto? facetContext, CancellationToken cancellationToken)
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
            if (facetContext is not null)
            {
                http.Request.Headers[FacetContextDto.Header] = facetContext.Encode();
            }
        }

        http.ODataFeature().RoutePrefix = profile.Prefix;
        return http;
    }

    // the CLR names the route writes for a row type: its EDM properties without the members denied to the user
    private static HashSet<string> Shown(Type type, IEdmModel model, Dictionary<string, IReadOnlySet<string>> denied)
    {
        var edm = model.FindDeclaredType(type.FullName) as IEdmStructuredType;
        var shown = new HashSet<string>(edm?.Properties().Select(p => ODataNames.Clr(model, p)) ?? [], StringComparer.OrdinalIgnoreCase);
        for (var t = edm; t is not null; t = t.BaseType())
        {
            shown.ExceptWith(denied.GetValueOrDefault(t.FullTypeName()) ?? new HashSet<string>());
        }

        return shown;
    }

    /// <summary>
    /// Clears what the route would not write on an entity of <paramref name="type"/>: through the setter, the field EF reads the member from, or the
    /// field of an auto-property; fields EF maps without a property too. Null when a member that holds data (one EF maps, or a hidden or denied one)
    /// has none of them: such an entity is not handed out. A getter EF does not map computes from the cleared members.
    /// </summary>
    private Action<object>? Clearer(Type type, IEdmModel model, Dictionary<string, IReadOnlySet<string>> denied)
    {
        var shown = Shown(type, model, denied);
        var entity = services.DbContextFor(type).Model.FindEntityType(type);
        var restricted = new HashSet<string>(registry.Entities.Where(e => e.EntityType.IsAssignableFrom(type)).SelectMany(e => e.HiddenProperties), StringComparer.OrdinalIgnoreCase);
        for (var t = type; t is not null; t = t.BaseType)
        {
            restricted.UnionWith(denied.GetValueOrDefault(t.FullName ?? string.Empty) ?? new HashSet<string>());
        }

        var clears = new List<Action<object>>();
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0 && !shown.Contains(p.Name)))
        {
            var mapped = (IReadOnlyPropertyBase?)entity?.FindProperty(property.Name) ?? (IReadOnlyPropertyBase?)entity?.FindNavigation(property.Name) ?? entity?.FindSkipNavigation(property.Name);
            if (property.SetMethod is not null)
            {
                var value = Default(property.PropertyType);
                clears.Add(row => property.SetValue(row, value));
            }
            else if ((mapped?.FieldInfo ?? Backing(property)) is { } field)
            {
                clears.Add(Clear(field));
            }
            else if (mapped is not null || restricted.Contains(property.Name))
            {
                return null;
            }
        }

        foreach (var fieldOnly in entity?.GetProperties().Where(p => p.PropertyInfo is null && p.FieldInfo is not null) ?? [])
        {
            clears.Add(Clear(fieldOnly.FieldInfo!));
        }

        return row => clears.ForEach(clear => clear(row));
    }

    private static Action<object> Clear(FieldInfo field)
    {
        var value = Default(field.FieldType);
        return row => field.SetValue(row, value);
    }

    private static object? Default(Type type) => type.IsValueType ? Activator.CreateInstance(type) : null;

    private static FieldInfo? Backing(PropertyInfo property) =>
        property.DeclaringType!.GetField($"<{property.Name}>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);

    // an entity as the route writes it without $select: its structural properties by the route's names, without the denied ones
    private static IReadOnlyDictionary<string, object?> Written(object row, IEdmModel model, Dictionary<string, IReadOnlySet<string>> denied)
    {
        var type = row.GetType();
        var shown = Shown(type, model, denied);
        return ((IEdmStructuredType)model.FindDeclaredType(type.FullName)).StructuralProperties()
            .Select(p => (p.Name, Property: shown.Contains(ODataNames.Clr(model, p)) ? type.GetProperty(ODataNames.Clr(model, p), BindingFlags.Public | BindingFlags.Instance) : null))
            .Where(p => p.Property is not null)
            .ToDictionary(p => p.Name, p => p.Property!.GetValue(row));
    }

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
