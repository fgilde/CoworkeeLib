using System.Collections;
using System.ComponentModel;
using System.Linq.Expressions;
using System.Reflection;
using Coworkee.Contracts.Data;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OData.Extensions;
using Microsoft.AspNetCore.OData.Query.Container;
using Microsoft.AspNetCore.OData.Query.Wrapper;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;
using Microsoft.OData;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using Nextended.Web.OData;

namespace Coworkee.OData;

internal sealed class ODataQueries(
    IServiceProvider services, ODataRoutes routes, ODataEntityRegistry registry, ICurrentUser user, ILogger<ODataQueries> logger,
    Microsoft.Extensions.Options.IOptions<CoworkeeODataOptions> limits, IHttpContextAccessor? accessor = null)
    : IODataQueries
{
    public async Task<Result<ODataQueryResult<T>>> QueryAsync<T>(string route, string entitySet, string? query, bool facets, FacetContextDto? facetContext,
        CancellationToken cancellationToken)
        where T : class
    {
        var (profile, set) = Find<T>(route, entitySet);
        if (await RefusedAsync<T>(set, cancellationToken) is { } refused)
        {
            return refused;
        }

        var http = Request(profile, set.EntitySet, query, facets, facetContext, cancellationToken);
        if (await ODataPipeline.PrepareAsync<T>(http, byKey: false) is { } refusal)
        {
            return Refused(refusal, nameof(query));
        }

        try
        {
            var model = ODataPipeline.Model(http.Request);
            var edmSet = model.EntityContainer.FindEntitySet(set.EntitySet);

            // what [EnableQuery] does on the route: validate with the configured limits (before the facets read the filter), page by the model's page
            // size, then apply in the scope's two steps
            var options = http.Request.ODataQueryOptions<T>(model, edmSet);
            var enableQuery = new ScopedEnableQueryAttribute(limits.Value, set.MaxNodeCount);
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

    public async Task<Result<T>> GetByKeyAsync<T>(string route, string entitySet, string key, string? query, CancellationToken cancellationToken)
        where T : class
    {
        var (profile, set) = Find<T>(route, entitySet);
        if (await RefusedAsync<T>(set, cancellationToken) is { } refused)
        {
            return refused;
        }

        object? value;
        try
        {
            value = TypeDescriptor.GetConverter(set.KeyType).ConvertFromInvariantString(key);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException or NotSupportedException)
        {
            return Error.Validation(nameof(key), $"{key} is no key of {set.EntitySet}.");
        }

        var http = Request(profile, set.EntitySet, query, false, null, cancellationToken);
        // as the key route: a single record, data filters that do not apply to details skip it; what it expands is a list with its list filters
        if (await ODataPipeline.PrepareAsync<T>(http, byKey: true) is { } refusal)
        {
            return Refused(refusal, nameof(query));
        }

        try
        {
            var model = ODataPipeline.Model(http.Request);
            var options = http.Request.ODataQueryOptions<T>(model, model.EntityContainer.FindEntitySet(set.EntitySet));
            var raw = options.RawValues;
            if (new[] { raw.Filter, raw.OrderBy, raw.Count, raw.Skip, raw.Top, raw.Apply, raw.Search, raw.SkipToken, raw.Compute }.Any(v => v is not null))
            {
                return Error.Validation(nameof(query), "A key read takes $select and $expand only.");
            }

            var enableQuery = new ScopedEnableQueryAttribute(limits.Value, set.MaxNodeCount);
            enableQuery.ValidateQuery(http.Request, options);
            var row = Expression.Parameter(typeof(T));
            var match = Expression.Lambda<Func<T, bool>>(Expression.Equal(Expression.Property(row, set.Key), Expression.Constant(value, set.KeyType)), row);
            var applied = enableQuery.ApplyQuery(ODataPipeline.Visible<T>(http).Where(match), options);
            object? item = null;
            if (applied is IAsyncEnumerable<object> pending)
            {
                await foreach (var found in pending.WithCancellation(cancellationToken))
                {
                    item = found;
                    break;
                }
            }
            else
            {
                item = applied.Cast<object>().FirstOrDefault();
            }

            if (item is null)
            {
                return Error.NotFound("odata.not_found", $"There is no {set.EntitySet} row {key}.");
            }

            var denied = (Dictionary<string, IReadOnlySet<string>>)http.Items[ODataEntityAccess.DeniedItem]!;
            var clearers = new Dictionary<Type, Action<object>?>();
            return (T)(item is ISelectExpandWrapper wrapper ? Entity(wrapper, denied, clearers) : Cleared(item, model, denied, clearers));
        }
        catch (UnclearableException)
        {
            // fails closed, like the list: no entity is handed out that keeps a member the user may not read
            return Error.Forbidden("odata.forbidden", $"{set.EntitySet} has a member you may not read that cannot be cleared on the entity; read it with QueryAsync.");
        }
        catch (ODataException exception)
        {
            return Error.Validation(nameof(query), exception.Message);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "An in-process OData key read on {Route}/{Set} failed.", profile.Prefix, set.EntitySet);
            return Error.Validation(nameof(query), "The query could not be run.");
        }
    }

    private (ODataRouteProfile Profile, ODataEntityRegistration Set) Find<T>(string route, string entitySet)
    {
        var profile = routes.Find(route) ?? throw new ArgumentException($"There is no OData route {route}.", nameof(route));
        return registry.Find(entitySet) is { } set && set.EntityType == typeof(T)
            ? (profile, set)
            : throw new ArgumentException($"{entitySet} is no OData set of {typeof(T).Name}.", nameof(entitySet));
    }

    // [Authorize] and the set's permission, as the controller has them
    private async Task<Error?> RefusedAsync<T>(ODataEntityRegistration set, CancellationToken cancellationToken)
    {
        if (!user.IsAuthenticated)
        {
            return Error.Unauthorized("odata.unauthorized", "Sign in to read OData sets.");
        }

        return await ODataEntityAccess.IsGrantedAsync(services, typeof(T), cancellationToken) ? null : Error.Forbidden("odata.forbidden", $"You may not read {set.EntitySet}.");
    }

    private static Error Refused(ODataPipeline.Refusal refusal, string field) => refusal.Status == StatusCodes.Status400BadRequest
        ? Error.Validation(field, refusal.Detail ?? "The query is not valid.")
        : Error.Forbidden("odata.forbidden", refusal.Detail ?? "The query reads a property you may not read.");

    private sealed class UnclearableException : Exception;

    // an entity cleared of what the route would not write
    private object Cleared(object entity, IEdmModel model, Dictionary<string, IReadOnlySet<string>> denied, Dictionary<Type, Action<object>?> clearers)
    {
        var type = entity.GetType();
        if (!clearers.TryGetValue(type, out var clear))
        {
            clearers[type] = clear = Clearer(type, model, denied);
        }

        (clear ?? throw new UnclearableException())(entity);
        return entity;
    }

    // the entity of a shaped key read: what the wrapper holds (selected and expanded members) by CLR names, without the denied members; the rest keeps
    // its constructor default. Expanded navigations become entities and collections of them.
    private object Entity(ISelectExpandWrapper wrapper, Dictionary<string, IReadOnlySet<string>> denied, Dictionary<Type, Action<object>?> clearers)
    {
        IEdmModel? model = null;
        IEdmStructuredType? type = null;
        var values = wrapper.ToDictionary((m, t) =>
        {
            (model, type) = (m, t);
            return Names.Instance;
        });
        var (edm, structured) = (model!, type!);
        var clr = edm.GetAnnotationValue<ClrTypeAnnotation>(structured)?.ClrType ?? throw new InvalidOperationException($"{structured.FullTypeName()} has no CLR type.");
        var entity = Activator.CreateInstance(clr, nonPublic: true)!;
        foreach (var (name, value) in values)
        {
            if (structured.FindProperty(name) is { } property && clr.GetProperty(ODataNames.Clr(edm, property), BindingFlags.Public | BindingFlags.Instance) is { } member)
            {
                Assign(entity, member, Value(value, member.PropertyType, denied, clearers));
            }
        }

        // the denied members (by the type and its bases) and what the route would not write are cleared as on a typed row
        return Cleared(entity, edm, denied, clearers);
    }

    private object? Value(object? value, Type target, Dictionary<string, IReadOnlySet<string>> denied, Dictionary<Type, Action<object>?> clearers)
    {
        if (value is ISelectExpandWrapper wrapper)
        {
            return Entity(wrapper, denied, clearers);
        }

        if (value is null || target.IsInstanceOfType(value) || value is string or byte[] || value is not IEnumerable items)
        {
            return value;
        }

        var element = target.IsArray
            ? target.GetElementType()!
            : target.GetInterfaces().Append(target).FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))?.GetGenericArguments()[0] ?? typeof(object);
        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(element))!;
        foreach (var item in items)
        {
            list.Add(Value(item, element, denied, clearers));
        }

        if (target.IsArray)
        {
            var array = Array.CreateInstance(element, list.Count);
            list.CopyTo(array, 0);
            return array;
        }

        if (target.IsInstanceOfType(list))
        {
            return list;
        }

        // HashSet<T>, Collection<T>, ...
        var collection = Activator.CreateInstance(target)!;
        var add = target.GetMethod("Add", [element])!;
        foreach (var item in list)
        {
            add.Invoke(collection, [item]);
        }

        return collection;
    }

    // through the setter, also a private one, else the field of an auto-property; a getter without either computes and is left alone
    private static void Assign(object entity, PropertyInfo property, object? value)
    {
        if (property.GetSetMethod(nonPublic: true) is { } setter)
        {
            setter.Invoke(entity, [value]);
        }
        else
        {
            Backing(property)?.SetValue(entity, value);
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
