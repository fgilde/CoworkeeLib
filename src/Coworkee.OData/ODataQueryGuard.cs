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

    /// <summary>
    /// Whether the options read a denied member. A clause OData cannot parse throws its <see cref="ODataException"/>: the caller answers 400
    /// rather than run a query the guard could not read.
    /// </summary>
    public static bool Denies(ODataQueryOptions options, IReadOnlyDictionary<string, IReadOnlySet<string>> denied)
    {
        if (denied.Count == 0)
        {
            return false;
        }

        var guard = new ODataQueryGuard(options.Context.Model, denied);
        var root = options.Context.ElementType as IEdmStructuredType;

        // $apply and $compute first: the other options may name what they add
        guard.Walk(options.Apply?.ApplyClause, root);
        guard.Walk(options.Compute?.ComputeClause, root);
        guard.Walk(options.Filter?.FilterClause, root);
        guard.Walk(options.OrderBy?.OrderByClause, root);
        guard.Select(options.SelectExpand?.SelectExpandClause, root);
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
                    Walk(path.ComputeOption, target);
                    Walk(path.FilterOption, target);
                    Walk(path.OrderByOption, target);
                    Select(path.SelectAndExpand, target);
                    break;
                case ExpandedCountSelectItem count:
                    Walk(count.FilterOption, Path(count.PathToNavigationProperty, type, viaNavigation: true));
                    break;
                case ExpandedReferenceSelectItem expand:
                    // an expanded navigation of a shared base: the serializer leaves it out per row, by the row's own set
                    var expanded = Path(expand.PathToNavigationProperty, type, viaNavigation: true);
                    Walk(expand.ApplyOption, expanded);
                    Walk(expand.ComputeOption, expanded);
                    Walk(expand.FilterOption, expanded);
                    Walk(expand.OrderByOption, expanded);
                    Walk(expand.CountOption, expanded);
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

    // every node of a parsed clause: every property and navigation a node holds is checked, whatever the node, so an unknown kind of node
    // fails closed; the type a member is read from is the node's source, else the type the clause is about (root, expanded target)
    private void Walk(object? node, IEdmStructuredType? context)
    {
        if (node is null or string || _found || !_seen.Add(node))
        {
            return;
        }

        if (node is SelectExpandClause clause)
        {
            Select(clause, context);
            return;
        }

        if (node is IEnumerable items)
        {
            foreach (var item in items)
            {
                Walk(item, context);
            }

            return;
        }

        if (node.GetType().Namespace?.StartsWith("Microsoft.OData.UriParser", StringComparison.Ordinal) != true)
        {
            return;
        }

        var values = new List<(string Name, object Value)>();
        foreach (var property in node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0))
        {
            try
            {
                if (property.GetValue(node) is { } value)
                {
                    values.Add((property.Name, value));
                }
            }
            catch (TargetInvocationException)
            {
            }
        }

        var source = TypeOf(values.FirstOrDefault(v => v.Name == "Source").Value as QueryNode) ?? context;
        foreach (var member in values.Select(v => v.Value).OfType<IEdmProperty>())
        {
            // a segment of a path ($expand, select) reads from the clause's type; a node from its source
            Check(member, node is ODataPathSegment ? context : source, derived: true);
        }

        foreach (var (_, value) in values.Where(v => v.Value is not IEdmElement))
        {
            Walk(value, context);
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
