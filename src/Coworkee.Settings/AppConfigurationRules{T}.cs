using System.Linq.Expressions;

namespace Coworkee.Settings;

/// <summary>Which properties of <typeparamref name="T"/> admins may not change (<see cref="Lock"/>), change only after unlocking them (<see cref="Protect"/>) or not even see (<see cref="Hide"/>).</summary>
public sealed class AppConfigurationRules<T>
{
    internal List<string> Locked { get; } = [];

    internal List<string> Protected { get; } = [];

    internal List<string> Hidden { get; } = [];

    /// <summary>Shown disabled; the server keeps what applies whatever comes in (values the host sets).</summary>
    public AppConfigurationRules<T> Lock(params Expression<Func<T, object?>>[] properties)
    {
        Locked.AddRange(properties.Select(PathOf));
        return this;
    }

    /// <summary>Changeable, but risky: the form asks to unlock the field first. Only the form protects, the server saves what comes in.</summary>
    public AppConfigurationRules<T> Protect(params Expression<Func<T, object?>>[] properties)
    {
        Protected.AddRange(properties.Select(PathOf));
        return this;
    }

    public AppConfigurationRules<T> Hide(params Expression<Func<T, object?>>[] properties)
    {
        Hidden.AddRange(properties.Select(PathOf));
        return this;
    }

    private static string PathOf(Expression<Func<T, object?>> property)
    {
        var names = new Stack<string>();
        var node = property.Body is UnaryExpression { NodeType: ExpressionType.Convert } convert ? convert.Operand : property.Body;
        while (node is MemberExpression member)
        {
            names.Push(member.Member.Name);
            node = member.Expression;
        }

        return node is ParameterExpression && names.Count > 0 ? string.Join(':', names) : throw new ArgumentException($"{property} is not a property path.", nameof(property));
    }
}
