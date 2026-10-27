using System.Collections;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OData.Extensions;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OData;
using Microsoft.OData.Edm;
using Microsoft.OData.UriParser;

namespace Coworkee.OData;

/// <summary>
/// Whether a query reads a property the user may not (<see cref="ODataEntityAccess.DeniedByTypeAsync"/>), checked on the clauses OData parsed:
/// every property and navigation the query really binds, in $filter, $orderby, $compute, $apply, $select and $expand with their nested options.
/// Spelling (<c>Featured/ Size</c>) and literals (<c>'a/Note'</c>) do not matter. A member is denied on the type it is read from, its bases and,
/// when read through a base type, every derived set that denies it.
/// </summary>
internal sealed class ODataQueryGuard
{
    private readonly IEdmModel _model;
    private readonly IReadOnlyDictionary<string, IReadOnlySet<string>> _denied;
    private readonly HashSet<object> _seen = new(ReferenceEqualityComparer.Instance);
    private bool _found;

    private ODataQueryGuard(IEdmModel model, IReadOnlyDictionary<string, IReadOnlySet<string>> denied) => (_model, _denied) = (model, denied);

    /// <summary>Whether the options read a denied member; a clause OData cannot parse is left to [EnableQuery], which refuses it.</summary>
    public static bool Denies(ODataQueryOptions options, IReadOnlyDictionary<string, IReadOnlySet<string>> denied)
    {
        if (denied.Count == 0)
        {
            return false;
        }

        var guard = new ODataQueryGuard(options.Context.Model, denied);
        try
        {
            // $apply and $compute first: the other options may name what they add
            guard.Walk(options.Apply?.ApplyClause);
            guard.Walk(options.Compute?.ComputeClause);
            guard.Walk(options.Filter?.FilterClause);
            guard.Walk(options.OrderBy?.OrderByClause);
            guard.Select(options.SelectExpand?.SelectExpandClause, options.Context.ElementType as IEdmStructuredType);
        }
        catch (ODataException)
        {
            return false;
        }

        return guard._found;
    }

    /// <summary>Whether an OData $filter on <paramref name="entityType"/> reads a denied member.</summary>
    public static bool Denies(IServiceProvider services, Type entityType, string? filter, IReadOnlyDictionary<string, IReadOnlySet<string>> denied) =>
        !string.IsNullOrWhiteSpace(filter) && Denies(Options(services, entityType, QueryString.Create("$filter", filter)), denied);

    /// <summary>Query options of a set read from a query string outside a request of the set's own route (exports, reports, the data tools).</summary>
    public static ODataQueryOptions Options(IServiceProvider services, Type entityType, QueryString query)
    {
        var http = new DefaultHttpContext { RequestServices = services };
        http.ODataFeature().RoutePrefix = CoworkeeODataModule.RoutePrefix;
        http.Request.QueryString = query;
        return new ODataQueryOptions(new ODataQueryContext(services.GetRequiredService<IEdmModel>(), entityType, new ODataPath()), http.Request);
    }

    private void Select(SelectExpandClause? clause, IEdmStructuredType? type)
    {
        foreach (var item in clause?.SelectedItems ?? [])
        {
            if (_found)
            {
                return;
            }

            switch (item)
            {
                case PathSelectItem path:
                    var target = Path(path.SelectedPath, type, viaNavigation: false);
                    Walk(path.ComputeOption);
                    Walk(path.FilterOption);
                    Walk(path.OrderByOption);
                    Select(path.SelectAndExpand, target);
                    break;
                case ExpandedCountSelectItem count:
                    Path(count.PathToNavigationProperty, type, viaNavigation: true);
                    Walk(count.FilterOption);
                    break;
                case ExpandedReferenceSelectItem expand:
                    // an expanded navigation of a shared base: the serializer leaves it out per row, by the row's own set
                    var expanded = Path(expand.PathToNavigationProperty, type, viaNavigation: true);
                    Walk(expand.ComputeOption);
                    Walk(expand.FilterOption);
                    Walk(expand.OrderByOption);
                    Walk(expand.CountOption);
                    if (expand is ExpandedNavigationSelectItem { SelectAndExpand: { } nested })
                    {
                        Select(nested, expanded);
                    }

                    break;
            }
        }
    }

    private IEdmStructuredType? Path(ODataPath? path, IEdmStructuredType? type, bool viaNavigation)
    {
        foreach (var segment in path ?? new ODataPath())
        {
            switch (segment)
            {
                case TypeSegment cast:
                    type = cast.EdmType.AsElementType() as IEdmStructuredType;
                    break;
                case PropertySegment property:
                    Check(property.Property, type, derived: true);
                    type = property.Property.Type.Definition.AsElementType() as IEdmStructuredType;
                    break;
                case NavigationPropertySegment navigation:
                    Check(navigation.NavigationProperty, type, derived: !viaNavigation);
                    type = navigation.NavigationProperty.ToEntityType();
                    break;
            }
        }

        return type;
    }

    // every node of a parsed clause; the members it binds are checked
    private void Walk(object? node)
    {
        if (node is null or string || _found || !_seen.Add(node))
        {
            return;
        }

        switch (node)
        {
            case SingleValuePropertyAccessNode access:
                Check(access.Property, TypeOf(access.Source), derived: true);
                break;
            case CollectionPropertyAccessNode access:
                Check(access.Property, TypeOf(access.Source), derived: true);
                break;
            case SingleComplexNode complex:
                Check(complex.Property, TypeOf(complex.Source), derived: true);
                break;
            case CollectionComplexNode complex:
                Check(complex.Property, TypeOf(complex.Source), derived: true);
                break;
            case SingleNavigationNode navigation:
                Check(navigation.NavigationProperty, TypeOf(navigation.Source), derived: true);
                break;
            case CollectionNavigationNode navigation:
                Check(navigation.NavigationProperty, TypeOf(navigation.Source), derived: true);
                break;
            case PropertySegment segment:
                Check(segment.Property, null, derived: true);
                break;
            case NavigationPropertySegment segment:
                Check(segment.NavigationProperty, null, derived: true);
                break;
            case SelectExpandClause clause:
                Select(clause, null);
                return;
        }

        if (node is IEnumerable items)
        {
            foreach (var item in items)
            {
                Walk(item);
            }

            return;
        }

        if (node.GetType().Namespace?.StartsWith("Microsoft.OData.UriParser", StringComparison.Ordinal) != true)
        {
            return;
        }

        foreach (var property in node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0))
        {
            if (property.PropertyType.Namespace?.StartsWith("Microsoft.OData.Edm", StringComparison.Ordinal) == true)
            {
                continue;
            }

            object? value;
            try
            {
                value = property.GetValue(node);
            }
            catch (TargetInvocationException)
            {
                continue;
            }

            Walk(value);
        }
    }

    private static IEdmStructuredType? TypeOf(QueryNode? source) => source switch
    {
        SingleValueNode single => single.TypeReference?.Definition as IEdmStructuredType,
        CollectionResourceNode collection => collection.ItemStructuredType?.StructuredDefinition(),
        _ => null,
    };

    // denied on the type it is read from and its bases; read through a base type, also on every derived set (the row may be one of them)
    private void Check(IEdmProperty property, IEdmStructuredType? type, bool derived)
    {
        type ??= property.DeclaringType;
        var types = new List<IEdmStructuredType>();
        for (var t = type; t is not null; t = t.BaseType())
        {
            types.Add(t);
        }

        if (derived)
        {
            types.AddRange(_model.FindAllDerivedTypes(type));
        }

        _found |= types.Any(t => _denied.TryGetValue(t.FullTypeName(), out var names) && names.Contains(property.Name));
    }
}
