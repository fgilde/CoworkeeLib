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
    /// An OData filter in CLR names in the model's names: every property path outside literals, from <paramref name="root"/>, from the element type
    /// of the collection a lambda variable ranges over (<c>Bins/any(b: b/Size gt 1)</c>), and through type casts (<c>Featured/Ns.Bin/Size</c>).
    /// Function names, keywords, typed literals and what it cannot resolve stay as they are.
    /// </summary>
    public static string Filter(IEdmModel model, IEdmStructuredType root, string filter)
    {
        var text = new System.Text.StringBuilder(filter.Length);
        var variables = new List<(string Name, IEdmStructuredType Type, int Depth)>();
        IEdmStructuredType? lambda = null;
        var depth = 0;
        for (var i = 0; i < filter.Length;)
        {
            var c = filter[i];
            if (c == '\'')
            {
                var literal = Literal().Match(filter, i);
                text.Append(literal.Value);
                i += Math.Max(1, literal.Length);
                continue;
            }

            if (c == '(')
            {
                depth++;
                text.Append(c);
                i++;
                if (lambda is not null && Variable().Match(filter, i) is { Success: true } variable)
                {
                    variables.Add((variable.Groups[1].Value, lambda, depth));
                    text.Append(variable.Value);
                    i += variable.Length;
                }

                lambda = null;
                continue;
            }

            if (c == ')')
            {
                variables.RemoveAll(v => v.Depth == depth);
                depth--;
            }

            var path = i > 0 && IsPart(filter[i - 1]) ? Match.Empty : PathToken().Match(filter, i);
            if (!path.Success || path.Index != i || path.Length == 0)
            {
                text.Append(c);
                i++;
                continue;
            }

            i += path.Length;
            var next = i < filter.Length ? filter[i] : '\0';
            var segments = path.Value.Split('/');
            if (next is '\'' or '-' || (segments.Length == 1 && (Keywords.Contains(path.Value) || next == '(')))
            {
                text.Append(path.Value); // a typed literal, guid, keyword or function
                continue;
            }

            // Bins/any( : the path to the collection, and the lambda that follows ranges over its elements
            var any = next == '(' && segments[^1] is "any" or "all";
            var resolved = segments[..^(any ? 1 : 0)];
            var variableOf = variables.LastOrDefault(v => v.Name == resolved[0]);
            var (translated, type) = Resolve(model, variableOf.Type ?? root, resolved, skipFirst: variableOf.Type is not null);
            text.Append(translated).Append(any ? "/" + segments[^1] : string.Empty);
            lambda = any ? type : null;
        }

        return text.ToString();
    }

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "eq", "ne", "gt", "ge", "lt", "le", "and", "or", "not", "has", "in", "add", "sub", "mul", "div", "mod", "true", "false", "null", "$it", "$this", "$root",
    };

    // segments through properties and casts, from the type; what it cannot resolve stays, and so does the rest after it
    private static (string Text, IEdmStructuredType? Type) Resolve(IEdmModel model, IEdmStructuredType? type, string[] segments, bool skipFirst)
    {
        var names = new List<string>(segments.Length);
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            if ((i == 0 && skipFirst) || type is null)
            {
                names.Add(segment);
                continue;
            }

            if (segment.Contains('.'))
            {
                type = model.FindDeclaredType(segment) as IEdmStructuredType;
                names.Add(segment);
                continue;
            }

            var property = Find(model, type, segment);
            type = property?.Type.Definition.AsElementType() as IEdmStructuredType;
            names.Add(property?.Name ?? segment);
            if (property is null)
            {
                names.AddRange(segments[(i + 1)..]);
                break;
            }
        }

        return (string.Join('/', names), type);
    }

    private static bool IsPart(char c) => char.IsLetterOrDigit(c) || c is '_' or '.' or '@' or '$' or '\'' or '-';

    private static IEdmProperty? Find(IEdmModel model, IEdmStructuredType type, string clrName) =>
        type.Properties().FirstOrDefault(p => Clr(model, p) == clrName)
        ?? type.Properties().FirstOrDefault(p => string.Equals(Clr(model, p), clrName, StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(@"[A-Za-z_]\w*")]
    private static partial Regex Segment();

    [GeneratedRegex(@"\G[A-Za-z_$][\w.]*(?:/[A-Za-z_$][\w.]*)*")]
    private static partial Regex PathToken();

    [GeneratedRegex(@"\G'(?:[^']|'')*'?")]
    private static partial Regex Literal();

    [GeneratedRegex(@"\G\s*([A-Za-z_]\w*)\s*:")]
    private static partial Regex Variable();
}
