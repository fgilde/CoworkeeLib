using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;

namespace Coworkee.Client.Blazor.Components.Query;

/// <summary>
/// Turns a query into a LINQ predicate on <c>T</c>, for in-memory rows or any <see cref="IQueryable{T}"/> (EF Core translates it).
/// Same meaning as <see cref="ODataQueryTranslator"/>: text ignores case, dates compare by whole days, a null navigation reads as null.
/// </summary>
public static class LinqQueryTranslator
{
    private static readonly MethodInfo ToLower = typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!;
    private static readonly MethodInfo Contains = typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;
    private static readonly MethodInfo StartsWith = typeof(string).GetMethod(nameof(string.StartsWith), [typeof(string)])!;
    private static readonly MethodInfo EndsWith = typeof(string).GetMethod(nameof(string.EndsWith), [typeof(string)])!;
    private static readonly MethodInfo Compare = typeof(string).GetMethod(nameof(string.Compare), [typeof(string), typeof(string)])!;

    public static Expression<Func<T, bool>> ToExpression<T>(QueryGroup root, IReadOnlyList<QueryField> fields, DateTimeOffset? now = null)
    {
        var it = Expression.Parameter(typeof(T), "it");
        var byName = fields.ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);
        var body = Group(root, it, byName, now ?? DateTimeOffset.Now) ?? Expression.Constant(true);
        return Expression.Lambda<Func<T, bool>>(body, it);
    }

    public static IQueryable<T> Where<T>(this IQueryable<T> query, QueryGroup root, IReadOnlyList<QueryField> fields, DateTimeOffset? now = null) =>
        root.IsEmpty ? query : query.Where(ToExpression<T>(root, fields, now));

    public static IEnumerable<T> Where<T>(this IEnumerable<T> rows, QueryGroup root, IReadOnlyList<QueryField> fields, DateTimeOffset? now = null) =>
        root.IsEmpty ? rows : rows.Where(ToExpression<T>(root, fields, now).Compile());

    private static Expression? Group(QueryGroup group, ParameterExpression it, Dictionary<string, QueryField> fields, DateTimeOffset now)
    {
        var parts = group.Children
            .Select(c => c is QueryGroup g ? Group(g, it, fields, now) : Rule((QueryRule)c, it, fields, now))
            .OfType<Expression>()
            .ToList();
        if (parts.Count == 0)
        {
            return null;
        }

        var joined = parts.Aggregate((a, b) => group.Logic == QueryLogic.And ? Expression.AndAlso(a, b) : Expression.OrElse(a, b));
        return group.Not ? Expression.Not(joined) : joined;
    }

    private static Expression? Rule(QueryRule rule, ParameterExpression it, Dictionary<string, QueryField> fields, DateTimeOffset now)
    {
        if (!QueryOperators.IsComplete(rule) || !fields.TryGetValue(rule.Field!, out var field))
        {
            return null;
        }

        var value = Member(it, field.Name);
        var type = Nullable.GetUnderlyingType(value.Type) ?? value.Type;
        if (rule.Operator is QueryOperator.IsNull or QueryOperator.IsNotNull)
        {
            var isNull = IsNull(value);
            return rule.Operator == QueryOperator.IsNull ? isNull : Expression.Not(isNull);
        }

        if (type == typeof(DateTimeOffset) || type == typeof(DateTime) || type == typeof(DateOnly))
        {
            if (!QueryOperators.IsList(rule.Operator))
            {
                return QueryDates.Range(rule, now) is { } range ? DateRange(value, type, range) : null;
            }
        }

        Expression Const(string text) => Expression.Constant(Parse(text, type), value.Type);
        if (field.Flags && type.IsEnum && rule.Operator is QueryOperator.Equal or QueryOperator.NotEqual or QueryOperator.In or QueryOperator.NotIn)
        {
            var flags = QueryOperators.IsList(rule.Operator) ? rule.Values! : [rule.Value!];
            var has = flags.Select(f => Has(value, type, f)).Aggregate(Expression.OrElse);
            return rule.Operator is QueryOperator.Equal or QueryOperator.In ? has : Expression.Not(has);
        }

        return rule.Operator switch
        {
            QueryOperator.IsTrue => Expression.Equal(value, Expression.Constant(true, value.Type)),
            QueryOperator.IsFalse => Expression.Equal(value, Expression.Constant(false, value.Type)),
            QueryOperator.Contains => Text(value, Contains, rule.Value!),
            QueryOperator.NotContains => Expression.Not(Text(value, Contains, rule.Value!)),
            QueryOperator.StartsWith => Text(value, StartsWith, rule.Value!),
            QueryOperator.EndsWith => Text(value, EndsWith, rule.Value!),
            QueryOperator.Equal => Expression.Equal(value, Const(rule.Value!)),
            QueryOperator.NotEqual => Expression.NotEqual(value, Const(rule.Value!)),
            QueryOperator.GreaterThan => Order(ExpressionType.GreaterThan, value, Const(rule.Value!)),
            QueryOperator.GreaterThanOrEqual => Order(ExpressionType.GreaterThanOrEqual, value, Const(rule.Value!)),
            QueryOperator.LessThan => Order(ExpressionType.LessThan, value, Const(rule.Value!)),
            QueryOperator.LessThanOrEqual => Order(ExpressionType.LessThanOrEqual, value, Const(rule.Value!)),
            QueryOperator.Between => Expression.AndAlso(Order(ExpressionType.GreaterThanOrEqual, value, Const(rule.Value!)), Order(ExpressionType.LessThanOrEqual, value, Const(rule.Value2!))),
            QueryOperator.NotBetween => Expression.OrElse(Order(ExpressionType.LessThan, value, Const(rule.Value!)), Order(ExpressionType.GreaterThan, value, Const(rule.Value2!))),
            QueryOperator.In => In(value, type, rule.Values!),
            QueryOperator.NotIn => Expression.Not(In(value, type, rule.Values!)),
            _ => null,
        };
    }

    /// <summary>A null-propagating member path: <c>Customer.Name</c> reads as null when the customer is null.</summary>
    public static Expression Member(Expression instance, string path)
    {
        var current = instance;
        foreach (var name in path.Split('.', '/'))
        {
            var property = current.Type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                ?? throw new ArgumentException($"{current.Type.Name} has no property {name}.", nameof(path));
            var access = Expression.Property(current, property);
            if (current == instance || current.Type.IsValueType)
            {
                current = access;
                continue;
            }

            var lifted = Lift(access.Type);
            current = Expression.Condition(Expression.Equal(current, Expression.Constant(null, current.Type)),
                Expression.Constant(null, lifted), access.Type == lifted ? access : Expression.Convert(access, lifted));
        }

        return current;
    }

    /// <summary>An invariant value as <paramref name="type"/>; enums by name or number.</summary>
    public static object Parse(string text, Type type) => type switch
    {
        _ when type.IsEnum => Enum.Parse(type, text, ignoreCase: true),
        _ when type == typeof(string) => text,
        _ when type == typeof(Guid) => Guid.Parse(text),
        _ when type == typeof(bool) => bool.Parse(text),
        _ when type == typeof(DateOnly) => DateOnly.FromDateTime(DateTime.Parse(text, CultureInfo.InvariantCulture)),
        _ when type == typeof(DateTimeOffset) => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture),
        _ when type == typeof(DateTime) => DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal),
        _ => Convert.ChangeType(text, type, CultureInfo.InvariantCulture),
    };

    private static Expression Has(Expression value, Type type, string flag)
    {
        var bits = Expression.Convert(value, value.Type == type ? typeof(long) : typeof(long?));
        var mask = Expression.Constant(Convert.ToInt64(Parse(flag, type), CultureInfo.InvariantCulture), bits.Type);
        return Expression.Equal(Expression.And(bits, mask), mask);
    }

    private static Expression IsNull(Expression value)
    {
        var isNull = value.Type.IsValueType && Nullable.GetUnderlyingType(value.Type) is null
            ? (Expression)Expression.Constant(false)
            : Expression.Equal(value, Expression.Constant(null, value.Type));
        return value.Type == typeof(string) ? Expression.OrElse(isNull, Expression.Equal(value, Expression.Constant(string.Empty))) : isNull;
    }

    // null text never matches, the way OData's string functions answer null
    private static Expression Text(Expression value, MethodInfo method, string text) =>
        Expression.AndAlso(Expression.NotEqual(value, Expression.Constant(null, typeof(string))),
            Expression.Call(Expression.Call(value, ToLower), method, Expression.Constant(text.ToLowerInvariant())));

    private static Expression Order(ExpressionType kind, Expression left, Expression right)
    {
        if (left.Type == typeof(string))
        {
            left = Expression.Call(Compare, left, right);
            right = Expression.Constant(0);
        }

        return Expression.MakeBinary(kind, left, right, liftToNull: false, method: null);
    }

    private static Expression In(Expression value, Type type, IReadOnlyList<string> values)
    {
        var array = Array.CreateInstance(value.Type, values.Count);
        for (var i = 0; i < values.Count; i++)
        {
            array.SetValue(Parse(values[i], type), i);
        }

        return Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), [value.Type], Expression.Constant(array), value);
    }

    private static Expression DateRange(Expression value, Type type, (DateTimeOffset? From, DateTimeOffset? To, bool Outside) range)
    {
        Expression Bound(DateTimeOffset at) => Expression.Constant(
            type == typeof(DateOnly) ? DateOnly.FromDateTime(at.DateTime) : type == typeof(DateTime) ? at.UtcDateTime : at, value.Type);
        var (from, to, outside) = range;
        if (outside)
        {
            return Expression.OrElse(Order(ExpressionType.LessThan, value, Bound(from!.Value)), Order(ExpressionType.GreaterThanOrEqual, value, Bound(to!.Value)));
        }

        var lower = from is { } f ? Order(ExpressionType.GreaterThanOrEqual, value, Bound(f)) : null;
        var upper = to is { } t ? Order(ExpressionType.LessThan, value, Bound(t)) : null;
        return (lower, upper) switch
        {
            ({ } l, { } u) => Expression.AndAlso(l, u),
            ({ } l, null) => l,
            (null, { } u) => u,
            _ => Expression.Constant(true),
        };
    }

    private static Type Lift(Type type) => type.IsValueType && Nullable.GetUnderlyingType(type) is null ? typeof(Nullable<>).MakeGenericType(type) : type;
}
