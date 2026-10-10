using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.OData.UriParser;

namespace Coworkee.Client.Blazor.ClientEntities;

/// <summary>
/// Turns OData $filter and $orderby text into LINQ over local rows. Paths are null safe, literals take the type of the property
/// they are compared with. Anything it does not know throws <see cref="NotSupportedException"/>, so callers can ask the server instead.
/// </summary>
public static class ODataLinq
{
    private const int MaxDepth = 100;

    public static Expression<Func<T, bool>> Filter<T>(string filter)
    {
        var it = Expression.Parameter(typeof(T), "it");
        var body = new Binder(it).Bind(Parse(filter));
        return Expression.Lambda<Func<T, bool>>(Binder.AsBool(body), it);
    }

    /// <summary>A null safe getter for a path like "Brand/Name", boxed.</summary>
    public static Func<T, object?> Path<T>(string path)
    {
        var it = Expression.Parameter(typeof(T), "it");
        var body = new Binder(it).Bind(Parse(path.Trim()));
        return Expression.Lambda<Func<T, object?>>(Expression.Convert(body, typeof(object)), it).Compile();
    }

    public static IEnumerable<T> OrderBy<T>(IEnumerable<T> rows, string? orderBy)
    {
        if (string.IsNullOrWhiteSpace(orderBy))
        {
            return rows;
        }

        IOrderedEnumerable<T>? ordered = null;
        foreach (var clause in orderBy.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = clause.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var key = Path<T>(parts[0]);
            var descending = parts.Length > 1 && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase);
            ordered = (ordered, descending) switch
            {
                (null, false) => rows.OrderBy(key, KeyComparer.Instance),
                (null, true) => rows.OrderByDescending(key, KeyComparer.Instance),
                (_, false) => ordered.ThenBy(key, KeyComparer.Instance),
                _ => ordered.ThenByDescending(key, KeyComparer.Instance),
            };
        }

        return ordered ?? rows;
    }

    /// <summary>Recognizes "Path eq literal", the shape of list facet options, so they can be counted in one pass.</summary>
    internal static bool TryEquality<T>(string filter, out string path, out object? value)
    {
        path = string.Empty;
        value = null;
        if (Parse(filter) is not BinaryOperatorToken { OperatorKind: BinaryOperatorKind.Equal, Left: EndPathToken left, Right: LiteralToken literal })
        {
            return false;
        }

        var it = Expression.Parameter(typeof(T), "it");
        var member = new Binder(it).Bind(left);
        path = PathText(left);
        value = Binder.ConvertValue(literal.Value, Nullable.GetUnderlyingType(member.Type) ?? member.Type);
        return true;
    }

    private static string PathText(PathToken token) =>
        (token.NextToken is PathToken next ? PathText(next) + "/" : string.Empty) + token.Identifier;

    private static QueryToken Parse(string text)
    {
        try
        {
            return new UriQueryExpressionParser(MaxDepth).ParseFilter(text);
        }
        catch (Microsoft.OData.ODataException exception)
        {
            throw new NotSupportedException(exception.Message, exception);
        }
    }

    private sealed class KeyComparer : IComparer<object?>
    {
        public static readonly KeyComparer Instance = new();

        public int Compare(object? x, object? y) => (x, y) switch
        {
            (null, null) => 0,
            (null, _) => -1,
            (_, null) => 1,
            (string a, string b) => string.Compare(a, b, StringComparison.CurrentCultureIgnoreCase),
            _ => Comparer<object>.Default.Compare(x, y),
        };
    }

    private sealed class Binder(ParameterExpression it)
    {
        private static readonly MethodInfo Contains = typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;
        private static readonly MethodInfo StartsWith = typeof(string).GetMethod(nameof(string.StartsWith), [typeof(string)])!;
        private static readonly MethodInfo EndsWith = typeof(string).GetMethod(nameof(string.EndsWith), [typeof(string)])!;
        private static readonly MethodInfo CompareStrings = typeof(string).GetMethod(nameof(string.CompareOrdinal), [typeof(string), typeof(string)])!;

        public Expression Bind(QueryToken token) => token switch
        {
            BinaryOperatorToken binary => Binary(binary),
            UnaryOperatorToken { OperatorKind: UnaryOperatorKind.Not } not => Expression.Not(AsBool(Bind(not.Operand))),
            UnaryOperatorToken negate => Expression.Negate(Bind(negate.Operand)),
            LiteralToken literal => Expression.Constant(literal.Value),
            PathToken path => Member(path.NextToken is null ? it : Bind(path.NextToken), path.Identifier),
            FunctionCallToken function => Function(function),
            InToken @in => In(@in),
            _ => throw new NotSupportedException($"{token.Kind} is not supported locally."),
        };

        public static Expression AsBool(Expression expression) =>
            expression.Type == typeof(bool?) ? Expression.Equal(expression, Expression.Constant(true, typeof(bool?))) : expression;

        public static object? ConvertValue(object? value, Type type)
        {
            if (value is null || type.IsInstanceOfType(value))
            {
                return value;
            }

            var text = Convert.ToString(value, CultureInfo.InvariantCulture)!;
            return type switch
            {
                _ when type.IsEnum => value is string ? Enum.Parse(type, text, ignoreCase: true) : Enum.ToObject(type, value),
                _ when type == typeof(Guid) => Guid.Parse(text),
                _ when type == typeof(DateTimeOffset) => value is DateTime date ? new DateTimeOffset(date) : DateTimeOffset.Parse(text, CultureInfo.InvariantCulture),
                _ when type == typeof(DateTime) => value is DateTimeOffset offset ? offset.UtcDateTime : DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal),
                _ when type == typeof(DateOnly) => DateOnly.Parse(text, CultureInfo.InvariantCulture),
                _ when type == typeof(string) => text,
                _ => Convert.ChangeType(value, type, CultureInfo.InvariantCulture),
            };
        }

        private Expression Member(Expression instance, string name)
        {
            var property = instance.Type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                ?? throw new NotSupportedException($"{instance.Type.Name} has no property {name}.");
            Expression access = Expression.Property(instance, property);
            if (instance == it || instance.Type.IsValueType)
            {
                return access;
            }

            var type = Lift(access.Type);
            return Expression.Condition(Expression.Equal(instance, Expression.Constant(null, instance.Type)), Expression.Constant(null, type), Expression.Convert(access, type));
        }

        private Expression Binary(BinaryOperatorToken token)
        {
            var left = Bind(token.Left);
            var right = Bind(token.Right);
            if (token.OperatorKind is BinaryOperatorKind.And or BinaryOperatorKind.Or)
            {
                return token.OperatorKind == BinaryOperatorKind.And ? Expression.AndAlso(AsBool(left), AsBool(right)) : Expression.OrElse(AsBool(left), AsBool(right));
            }

            (left, right) = Unify(left, right);
            if (left.Type == typeof(string) && token.OperatorKind is not (BinaryOperatorKind.Equal or BinaryOperatorKind.NotEqual))
            {
                left = Expression.Call(CompareStrings, left, right);
                right = Expression.Constant(0);
            }

            return token.OperatorKind switch
            {
                BinaryOperatorKind.Equal => Expression.Equal(left, right),
                BinaryOperatorKind.NotEqual => Expression.NotEqual(left, right),
                BinaryOperatorKind.GreaterThan => Expression.GreaterThan(left, right, liftToNull: false, method: null),
                BinaryOperatorKind.GreaterThanOrEqual => Expression.GreaterThanOrEqual(left, right, liftToNull: false, method: null),
                BinaryOperatorKind.LessThan => Expression.LessThan(left, right, liftToNull: false, method: null),
                BinaryOperatorKind.LessThanOrEqual => Expression.LessThanOrEqual(left, right, liftToNull: false, method: null),
                BinaryOperatorKind.Add => Expression.Add(left, right),
                BinaryOperatorKind.Subtract => Expression.Subtract(left, right),
                BinaryOperatorKind.Multiply => Expression.Multiply(left, right),
                BinaryOperatorKind.Divide => Expression.Divide(left, right),
                BinaryOperatorKind.Modulo => Expression.Modulo(left, right),
                _ => throw new NotSupportedException($"{token.OperatorKind} is not supported locally."),
            };
        }

        private static (Expression Left, Expression Right) Unify(Expression left, Expression right)
        {
            if (left.Type == right.Type)
            {
                return (left, right);
            }

            if (right is ConstantExpression constant)
            {
                return Align(left, constant);
            }

            if (left is ConstantExpression leftConstant)
            {
                var (r, l) = Align(right, leftConstant);
                return (l, r);
            }

            var type = Wider(Nullable.GetUnderlyingType(left.Type) ?? left.Type, Nullable.GetUnderlyingType(right.Type) ?? right.Type);
            if (Nullable.GetUnderlyingType(left.Type) is not null || Nullable.GetUnderlyingType(right.Type) is not null)
            {
                type = Lift(type);
            }

            return (Expression.Convert(left, type), Expression.Convert(right, type));
        }

        private static (Expression Member, Expression Constant) Align(Expression member, ConstantExpression constant)
        {
            if (constant.Value is null)
            {
                var nullable = Lift(member.Type);
                return (member.Type == nullable ? member : Expression.Convert(member, nullable), Expression.Constant(null, nullable));
            }

            var target = Nullable.GetUnderlyingType(member.Type) ?? member.Type;
            if (IsNumeric(target) && IsNumeric(constant.Type) && Wider(target, constant.Type) != target)
            {
                // a property of int compared with 4.5: compare as the wider type instead of truncating the literal
                var wide = member.Type == target ? Wider(target, constant.Type) : Lift(Wider(target, constant.Type));
                return (Expression.Convert(member, wide), Expression.Constant(ConvertValue(constant.Value, Wider(target, constant.Type)), wide));
            }

            return (member, Expression.Constant(ConvertValue(constant.Value, target), member.Type));
        }

        private Expression Function(FunctionCallToken token)
        {
            var args = token.Arguments.Select(a => Bind(a.ValueToken)).ToArray();
            return (token.Name.ToLowerInvariant(), args.Length) switch
            {
                ("contains", 2) => Call(Contains, args),
                ("startswith", 2) => Call(StartsWith, args),
                ("endswith", 2) => Call(EndsWith, args),
                ("tolower", 1) => NullSafe(args[0], s => Expression.Call(s, nameof(string.ToLowerInvariant), null)),
                ("toupper", 1) => NullSafe(args[0], s => Expression.Call(s, nameof(string.ToUpperInvariant), null)),
                ("trim", 1) => NullSafe(args[0], s => Expression.Call(s, nameof(string.Trim), null)),
                ("length", 1) => NullSafe(args[0], s => Expression.Property(s, nameof(string.Length))),
                ("year" or "month" or "day" or "hour" or "minute" or "second", 1) =>
                    NullSafe(args[0], d => Expression.Property(d, char.ToUpperInvariant(token.Name[0]) + token.Name[1..].ToLowerInvariant())),
                _ => throw new NotSupportedException($"{token.Name} is not supported locally."),
            };
        }

        // OData string functions on null are false, never an exception
        private static Expression Call(MethodInfo method, Expression[] args) =>
            Expression.Call(Expression.Coalesce(Text(args[0]), Expression.Constant(string.Empty)), method, Expression.Coalesce(Text(args[1]), Expression.Constant(string.Empty)));

        private static Expression Text(Expression expression) =>
            expression.Type == typeof(string) ? expression : throw new NotSupportedException("String functions need text.");

        private static Expression NullSafe(Expression value, Func<Expression, Expression> map)
        {
            var underlying = Nullable.GetUnderlyingType(value.Type);
            var target = underlying is null ? value : Expression.Property(value, nameof(Nullable<int>.Value));
            var mapped = map(target);
            if (value.Type.IsValueType && underlying is null)
            {
                return mapped;
            }

            var type = Lift(mapped.Type);
            return Expression.Condition(Expression.Equal(value, Expression.Constant(null, value.Type)), Expression.Constant(null, type), Expression.Convert(mapped, type));
        }

        private Expression In(InToken token)
        {
            var left = Bind(token.Left);
            if (token.Right is not LiteralToken { Value: string list })
            {
                throw new NotSupportedException("Only literal lists are supported in 'in'.");
            }

            var type = left.Type;
            var values = Array.CreateInstance(type, 0).GetType();
            var items = Split(list).Select(item => ConvertValue(Parse(item) is LiteralToken literal ? literal.Value : throw new NotSupportedException(item),
                Nullable.GetUnderlyingType(type) ?? type)).ToArray();
            var array = Array.CreateInstance(type, items.Length);
            for (var i = 0; i < items.Length; i++)
            {
                array.SetValue(items[i], i);
            }

            return Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), [type], Expression.Constant(array, values), left);
        }

        private static IEnumerable<string> Split(string list)
        {
            var text = list.Trim();
            text = text.StartsWith('(') && text.EndsWith(')') ? text[1..^1] : text;
            var start = 0;
            var quoted = false;
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == '\'')
                {
                    quoted = !quoted;
                }
                else if (text[i] == ',' && !quoted)
                {
                    yield return text[start..i].Trim();
                    start = i + 1;
                }
            }

            if (text.Length > start)
            {
                yield return text[start..].Trim();
            }
        }

        private static Type Lift(Type type) => type.IsValueType && Nullable.GetUnderlyingType(type) is null ? typeof(Nullable<>).MakeGenericType(type) : type;

        private static readonly Type[] Numeric = [typeof(byte), typeof(short), typeof(int), typeof(long), typeof(float), typeof(double), typeof(decimal)];

        private static bool IsNumeric(Type type) => Array.IndexOf(Numeric, type) >= 0;

        private static Type Wider(Type a, Type b)
        {
            if (!IsNumeric(a) || !IsNumeric(b))
            {
                throw new NotSupportedException($"{a.Name} and {b.Name} cannot be compared locally.");
            }

            // decimal wins over float and double: properties of money compare exactly
            return a == typeof(decimal) || b == typeof(decimal) ? typeof(decimal) : Array.IndexOf(Numeric, a) >= Array.IndexOf(Numeric, b) ? a : b;
        }
    }
}
