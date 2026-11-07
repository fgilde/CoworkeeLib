using System.Linq.Expressions;

namespace Coworkee.Client.Blazor.ClientEntities.Sqlite;

/// <summary>
/// The values of a filter as parameters: EF Core keeps a compiled query per shape and constant values, so every new value of a filter (another
/// search text, another chosen option) compiled every statement again, a tenth to a third of a second each in the browser. A value read from an
/// object's property becomes a parameter, and the next value runs the compiled query.
/// </summary>
internal static class Parameters
{
    public static Expression<Func<T, bool>> Of<T>(Expression<Func<T, bool>> filter) => (Expression<Func<T, bool>>)Visitor.Instance.Visit(filter);

    private sealed class Visitor : ExpressionVisitor
    {
        public static readonly Visitor Instance = new();

        protected override Expression VisitConstant(ConstantExpression node)
        {
            var plain = Nullable.GetUnderlyingType(node.Type) ?? node.Type;
            if (node.Value is null || !(plain.IsPrimitive || plain.IsEnum || plain == typeof(string) || plain == typeof(Guid) || plain == typeof(decimal)
                || plain == typeof(DateTime) || plain == typeof(DateTimeOffset) || plain == typeof(DateOnly) || plain == typeof(TimeOnly) || plain == typeof(TimeSpan)))
            {
                return node;
            }

            var box = Activator.CreateInstance(typeof(Box<>).MakeGenericType(node.Type), node.Value)!;
            return Expression.Property(Expression.Constant(box), nameof(Box<int>.Value));
        }
    }

    private sealed class Box<TValue>(TValue value)
    {
        public TValue Value { get; } = value;
    }
}
