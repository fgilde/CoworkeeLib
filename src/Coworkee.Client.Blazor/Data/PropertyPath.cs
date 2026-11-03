using System.Linq.Expressions;
using System.Reflection;

namespace Coworkee.Client.Blazor.Data;

/// <summary>Property paths of a row type with '.' (<c>Customer.Name</c>): their type and a getter that answers null where a step is null.</summary>
public static class PropertyPath
{
    /// <summary>The type at the end of the path, or null when a step is no public property.</summary>
    public static Type? TypeOf(Type root, string path)
    {
        var type = root;
        foreach (var name in path.Split('.'))
        {
            if (type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase) is not { } property)
            {
                return null;
            }

            type = property.PropertyType;
        }

        return type;
    }

    public static Func<T, object?> Getter<T>(string path)
    {
        var row = Expression.Parameter(typeof(T), "row");
        Expression body = row;
        Expression? guard = null;
        foreach (var name in path.Split('.'))
        {
            var property = body.Type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                ?? throw new ArgumentException($"{body.Type.Name} has no property {name}.", nameof(path));
            if (!body.Type.IsValueType || Nullable.GetUnderlyingType(body.Type) is not null)
            {
                var isNull = Expression.Equal(body, Expression.Constant(null, body.Type));
                guard = guard is null ? isNull : Expression.OrElse(guard, isNull);
            }

            body = Expression.Property(body, property);
        }

        Expression value = Expression.Convert(body, typeof(object));
        if (guard is not null)
        {
            value = Expression.Condition(guard, Expression.Constant(null), value);
        }

        return Expression.Lambda<Func<T, object?>>(value, row).Compile();
    }
}
