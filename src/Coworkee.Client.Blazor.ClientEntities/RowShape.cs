using System.Collections;
using System.Reflection;

namespace Coworkee.Client.Blazor.ClientEntities;

/// <summary>
/// How rows of a type split into columns and come back from them. Properties of simple types are columns, nested objects are
/// flattened ("Customer/Name") with a column that says whether the object is there. Null for types with other properties.
/// </summary>
internal sealed class RowShape
{
    /// <summary>Ends the path of the column that says whether a nested object is there.</summary>
    public const string Present = "?";

    private const int MaxDepth = 4;

    private readonly Node _root;

    private RowShape(Node root)
    {
        _root = root;
        Columns = [.. Paths(root)];
    }

    public IReadOnlyList<string> Columns { get; }

    public static RowShape? Of(Type type) => Describe(type, string.Empty, 0) is { } root ? new RowShape(root) : null;

    public object Create(IReadOnlyDictionary<string, Column> columns, int row) => Create(_root, columns, row);

    /// <summary>$select of the scalar properties and an $expand that selects the same of each nested object.</summary>
    public (string Select, string? Expand) Query(string? expand)
    {
        var names = (expand ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var nested = names.Select(name => _root.Members.FirstOrDefault(m => m.Nested is not null && m.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } member
            ? $"{member.Name}($select={Scalars(member.Nested!)})"
            : name);
        return (Scalars(_root), names.Length == 0 ? null : string.Join(",", nested));
    }

    private static string Scalars(Node node) => string.Join(",", node.Members.Where(m => m.Nested is null).Select(m => m.Name));

    private static IEnumerable<string> Paths(Node node) =>
        node.Members.SelectMany(m => m.Nested is { } nested ? Paths(nested).Prepend(m.Path + Present) : Enumerable.Repeat(m.Path, 1));

    private static object Create(Node node, IReadOnlyDictionary<string, Column> columns, int row)
    {
        var values = node.Members.Select(m => m.Nested is { } nested
            ? (bool)columns[m.Path + Present].Value(row)! ? Create(nested, columns, row) : null
            : columns[m.Path].Value(row)).ToArray();
        if (node.Constructor is { } constructor)
        {
            return constructor.Invoke([.. node.Parameters.Select(i => values[i])]);
        }

        var instance = Activator.CreateInstance(node.Type)!;
        for (var i = 0; i < values.Length; i++)
        {
            node.Members[i].Property.SetValue(instance, values[i]);
        }

        return instance;
    }

    private static Node? Describe(Type type, string prefix, int depth)
    {
        var members = new List<Member>();
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetMethod is not null && p.GetIndexParameters().Length == 0))
        {
            var path = prefix + property.Name;
            if (Column.Supports(property.PropertyType))
            {
                members.Add(new Member(property.Name, path, property, null));
            }
            else if (depth < MaxDepth && IsObject(property.PropertyType) && Describe(property.PropertyType, path + "/", depth + 1) is { } nested)
            {
                members.Add(new Member(property.Name, path, property, nested));
            }
            else
            {
                return null;
            }
        }

        return Construct(type, members);
    }

    private static bool IsObject(Type type) =>
        type.IsClass && !type.IsAbstract && type != typeof(object) && !typeof(IEnumerable).IsAssignableFrom(type);

    // a record's primary constructor, or a parameterless one with setters
    private static Node? Construct(Type type, List<Member> members)
    {
        foreach (var constructor in type.GetConstructors().OrderByDescending(c => c.GetParameters().Length))
        {
            var parameters = constructor.GetParameters()
                .Select(p => members.FindIndex(m => m.Name.Equals(p.Name, StringComparison.OrdinalIgnoreCase) && m.Property.PropertyType == p.ParameterType))
                .ToArray();
            if (parameters.Any(i => i < 0))
            {
                continue;
            }

            if (parameters.Length == members.Count)
            {
                return new Node(type, members, constructor, parameters);
            }

            if (parameters.Length == 0 && members.All(m => m.Property.SetMethod is { IsPublic: true }))
            {
                return new Node(type, members, null, []);
            }
        }

        return null;
    }

    private sealed record Node(Type Type, IReadOnlyList<Member> Members, ConstructorInfo? Constructor, int[] Parameters);

    private sealed record Member(string Name, string Path, PropertyInfo Property, Node? Nested);
}
