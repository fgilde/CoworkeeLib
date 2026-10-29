using System.Text.RegularExpressions;
using Coworkee.Contracts.Data;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;

namespace Coworkee.OData;

/// <summary>
/// Names between the CLR and a route's model. The rules (hidden, protected, computed permissions) are kept by CLR name, and every check compares
/// <see cref="Clr"/>, so a route that renames a property (lower camel case, alias) denies it the same way.
/// </summary>
internal static partial class ODataNames
{
    /// <summary>The CLR name of a property of the model: the renamed property's own name, else the EDM name (computed properties only change case).</summary>
    public static string Clr(IEdmModel model, IEdmProperty property) =>
        model.GetAnnotationValue<ClrPropertyInfoAnnotation>(property)?.ClrPropertyInfo.Name ?? property.Name;

    /// <summary>Facets in the route's names: field, value path and the OData filter of every option, so a client can send them back on the same route.</summary>
    public static List<FacetGroupDto> ToRoute(List<FacetGroupDto> groups, IEdmModel model, Type entityType)
    {
        if (model.FindDeclaredType(entityType.FullName) is not IEdmStructuredType root)
        {
            return groups;
        }

        return
        [
            .. groups.Select(g => g with
            {
                Field = Path(model, root, g.Field),
                ValuePath = g.ValuePath is null ? null : Path(model, root, g.ValuePath),
                Options = [.. g.Options.Select(o => o.OData is null ? o : o with { OData = Filter(model, root, o.OData) })],
            }),
        ];
    }

    /// <summary>A property path in CLR names (<c>Bin.Size</c>, <c>it/Bin/Size</c>) in the model's names; segments it cannot resolve stay.</summary>
    public static string Path(IEdmModel model, IEdmStructuredType root, string path)
    {
        var type = root;
        return Segment().Replace(path, m =>
        {
            if (type is null || (m.Index == 0 && m.Value == "it"))
            {
                return m.Value;
            }

            var property = Find(model, type, m.Value);
            type = property?.Type.Definition.AsElementType() as IEdmStructuredType;
            return property?.Name ?? m.Value;
        });
    }

    /// <summary>
    /// An OData filter in CLR names in the model's names: every property path outside literals that starts at <paramref name="root"/>. Lambda variables
    /// (<c>b: b/Size</c>) and type casts are left as they are; facet filters have none.
    /// </summary>
    public static string Filter(IEdmModel model, IEdmStructuredType root, string filter) =>
        Token().Replace(filter, m => m.Value[0] == '\'' || Find(model, root, m.Value.Split('/')[0]) is null ? m.Value : Path(model, root, m.Value));

    private static IEdmProperty? Find(IEdmModel model, IEdmStructuredType type, string clrName) =>
        type.Properties().FirstOrDefault(p => Clr(model, p) == clrName)
        ?? type.Properties().FirstOrDefault(p => string.Equals(Clr(model, p), clrName, StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(@"[A-Za-z_]\w*")]
    private static partial Regex Segment();

    // a quoted literal, or a path not part of a number, guid, qualified name, typed literal or function call
    [GeneratedRegex(@"'(?:[^']|'')*'|(?<![\w.@$'-])[A-Za-z_]\w*(?:/[A-Za-z_]\w*)*(?![\w(.'-])")]
    private static partial Regex Token();
}
