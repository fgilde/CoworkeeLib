using System.Linq.Expressions;

namespace Coworkee.Client.Blazor.ClientEntities.Sqlite;

/// <summary>
/// The values of a filter as parameters: EF Core keeps a compiled query per shape and constant values, so every new value of a filter (another
/// search text, another chosen option) compiled every statement again, a tenth to a third of a second each in the browser. A value read from an
/// object's property becomes a parameter, and the next value runs the compiled query.
/// <para>
/// Also "Flags has 'Gift'": the copy keeps enums by name ("Express, Gift"), so the bits ODataLinq compares (<c>((long)Flags &amp; 2) == 2</c>) read 0 from
/// the text and matched nothing. It becomes "Flags in (every value with that flag)", which compares names.
/// </para>
/// </summary>
internal static class Parameters
{
    // ponytail: the values with a flag are listed in full; an enum with more combinations goes to the server (NotSupportedException),
    // a LIKE on the names if one needs it locally
    private const int MaxFlagValues = 4096;

    private static readonly System.Reflection.MethodInfo Contains = typeof(Enumerable).GetMethods()
        .Single(m => m.Name == nameof(Enumerable.Contains) && m.GetParameters().Length == 2);

    public static Expression<Func<T, bool>> Of<T>(Expression<Func<T, bool>> filter) => (Expression<Func<T, bool>>)Visitor.Instance.Visit(filter);

    // every combination of the enum's members that has all bits of the flag
    private static Array WithFlag(Type member, Type enumType, long flag)
    {
        var values = new HashSet<long> { 0 };
        foreach (var value in Enum.GetValues(enumType).Cast<object>().Select(v => Convert.ToInt64(v, System.Globalization.CultureInfo.InvariantCulture)).Where(v => v != 0))
        {
            values.UnionWith(values.Select(v => v | value).ToList());
            if (values.Count > MaxFlagValues)
            {
                throw new NotSupportedException($"{enumType.Name} has too many flag combinations to compare locally.");
            }
        }

        var matching = values.Where(v => (v & flag) == flag).ToList();
        var array = Array.CreateInstance(member, matching.Count);
        for (var i = 0; i < matching.Count; i++)
        {
            array.SetValue(Enum.ToObject(enumType, matching[i]), i);
        }

        return array;
    }

    private sealed class Visitor : ExpressionVisitor
    {
        public static readonly Visitor Instance = new();

        protected override Expression VisitBinary(BinaryExpression node)
        {
            if (node is
                {
                    NodeType: ExpressionType.Equal,
                    Left: BinaryExpression { NodeType: ExpressionType.And, Left: UnaryExpression { NodeType: ExpressionType.Convert, Operand: var member }, Right: ConstantExpression { Value: long flag } },
                }
                && (Nullable.GetUnderlyingType(member.Type) ?? member.Type) is { IsEnum: true } enumType)
            {
                return Expression.Call(Contains.MakeGenericMethod(member.Type), Expression.Constant(WithFlag(member.Type, enumType, flag)), member);
            }

            return base.VisitBinary(node);
        }

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
