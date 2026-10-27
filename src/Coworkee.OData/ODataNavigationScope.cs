using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.OData;

/// <summary>
/// Row filters and set permissions for navigations: a query over a set reaches other rows through its navigations ($expand, $filter with
/// any/all, $orderby on a path, report fields, search fields), and <see cref="Apply{T}"/> makes each of them show only what the target's own
/// set lists for the user. A collection gets <c>.Where(filters)</c>, a reference becomes null when its row is hidden. A target set the user
/// may not read shows nothing, and so does a target whose filters do more than <c>Where</c> (they cannot be applied inside another query).
/// </summary>
public sealed class ODataNavigationScope
{
    // queries VisibleAsync built or Apply rewrote: their row filters are applied already, the rewrite leaves them alone
    private static readonly ConditionalWeakTable<Expression, object> Roots = new();

    private readonly Dictionary<Type, LambdaExpression> _predicates = [];
    private readonly HashSet<(Type?, string)> _navigations = [];

    private ODataNavigationScope()
    {
    }

    /// <summary>A scope that changes nothing, for queries without navigations.</summary>
    internal static ODataNavigationScope None { get; } = new();

    /// <summary>Navigation targets whose set the user may not read.</summary>
    internal HashSet<Type> Forbidden { get; } = [];

    /// <summary>Navigation targets whose row filters do more than <c>Where</c>.</summary>
    internal HashSet<Type> Unfilterable { get; } = [];

    /// <summary>The filters and permissions of every entity type reachable through navigations from <paramref name="root"/>.</summary>
    public static async Task<ODataNavigationScope> ForAsync(IServiceProvider services, Type root, CancellationToken cancellationToken)
    {
        var scope = new ODataNavigationScope();
        var registry = services.GetService<ODataEntityRegistry>();
        var reachable = scope.Reachable(services, root);
        foreach (var type in reachable.Select(t => t.ClrType).Distinct())
        {
            if (registry?.Find(type) is not null && !await ODataEntityAccess.IsGrantedAsync(services, type, cancellationToken))
            {
                scope.Forbidden.Add(type);
                scope._predicates[type] = Nothing(type);
                continue;
            }

            var harvest = await (Task<(bool, LambdaExpression?)>)HarvestMethod.MakeGenericMethod(type).Invoke(null, [services, cancellationToken])!;
            if (!harvest.Item1)
            {
                scope.Unfilterable.Add(type);
                scope._predicates[type] = Nothing(type);
            }
            else if (harvest.Item2 is { } predicate)
            {
                scope._predicates[type] = predicate;
            }
        }

        scope.AddDerived(reachable);
        return scope;
    }

    /// <summary>
    /// A navigation to a base type (Invoice.Shipment of ShipmentBase) also reads rows of the derived sets: each derived type with rules adds
    /// <c>!(x is D) || rules((D)x)</c> to the base, so a shipment hidden in its own set is hidden there too.
    /// </summary>
    private void AddDerived(IEnumerable<Microsoft.EntityFrameworkCore.Metadata.IEntityType> types)
    {
        foreach (var type in types.Where(t => t.GetDerivedTypes().Any()))
        {
            var row = Expression.Parameter(type.ClrType, "x");
            var conditions = type.GetDerivedTypes()
                .Where(d => _predicates.ContainsKey(d.ClrType))
                .Select(d => (Expression)Expression.OrElse(Expression.Not(Expression.TypeIs(row, d.ClrType)),
                    ReplacingExpressionVisitor.Replace(_predicates[d.ClrType].Parameters[0], Expression.Convert(row, d.ClrType), _predicates[d.ClrType].Body)))
                .ToList();
            if (conditions.Count == 0)
            {
                continue;
            }

            if (_predicates.TryGetValue(type.ClrType, out var own))
            {
                conditions.Insert(0, ReplacingExpressionVisitor.Replace(own.Parameters[0], row, own.Body));
            }

            _predicates[type.ClrType] = Expression.Lambda(conditions.Aggregate(Expression.AndAlso), row);
        }
    }

    /// <summary>The query with every navigation in it (outside the visible rows it started from) narrowed to the rows its target shows.</summary>
    public IQueryable<T> Apply<T>(IQueryable<T> query) => (IQueryable<T>)Apply((IQueryable)query);

    /// <inheritdoc cref="Apply{T}"/>
    public IQueryable Apply(IQueryable query)
    {
        if (_predicates.Count == 0)
        {
            return query;
        }

        var rewritten = query.Provider.CreateQuery(new Rewriter(this).Visit(query.Expression)!);
        Root(rewritten.Expression);
        return rewritten;
    }

    /// <summary>Marks the rows of a query as visible already: <see cref="Apply"/> does not touch navigations inside it (the set's own row filters).</summary>
    internal static void Root(Expression expression) => Roots.AddOrUpdate(expression, true);

    private List<Microsoft.EntityFrameworkCore.Metadata.IEntityType> Reachable(IServiceProvider services, Type root)
    {
        if (services.DbContextFor(root).Model.FindEntityType(root) is not { } start)
        {
            return [];
        }

        var seen = new HashSet<Microsoft.EntityFrameworkCore.Metadata.IEntityType>();
        var queue = new Queue<Microsoft.EntityFrameworkCore.Metadata.IEntityType>([start]);
        while (queue.TryDequeue(out var type))
        {
            foreach (var navigation in type.GetNavigations().Cast<Microsoft.EntityFrameworkCore.Metadata.INavigationBase>().Concat(type.GetSkipNavigations()))
            {
                if (navigation.PropertyInfo is { } property)
                {
                    _navigations.Add((property.DeclaringType, property.Name));
                }

                // derived types too: their rows come through a navigation to the base, their own navigations through a cast
                foreach (var target in navigation.TargetEntityType.GetDerivedTypesInclusive().Where(seen.Add))
                {
                    queue.Enqueue(target);
                }
            }
        }

        return [.. seen.Where(t => !t.ClrType.IsGenericType)];
    }

    private static LambdaExpression Nothing(Type type) => Expression.Lambda(Expression.Constant(false), Expression.Parameter(type, "x"));

    private static readonly MethodInfo HarvestMethod = typeof(ODataNavigationScope).GetMethod(nameof(HarvestAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>The row filters of <typeparamref name="T"/> as one predicate: run on an empty query, every step they add must be a Where.</summary>
    private static async Task<(bool Harvested, LambdaExpression? Predicate)> HarvestAsync<T>(IServiceProvider services, CancellationToken cancellationToken)
        where T : class
    {
        var filters = services.GetServices<IODataEntityFilter<T>>().ToList();
        if (filters.Count == 0)
        {
            return (true, null);
        }

        var seed = Enumerable.Empty<T>().AsQueryable();
        var query = seed;
        foreach (var filter in filters)
        {
            query = await filter.ApplyAsync(query, cancellationToken);
        }

        var row = Expression.Parameter(typeof(T), "x");
        var conditions = new List<Expression>();
        var step = query.Expression;
        while (step != seed.Expression)
        {
            if (step is not MethodCallExpression { Method.Name: nameof(Queryable.Where) } call || call.Method.DeclaringType != typeof(Queryable)
                || call.Arguments[1] is not UnaryExpression { Operand: LambdaExpression { Parameters.Count: 1 } where })
            {
                return (false, null);
            }

            conditions.Add(ReplacingExpressionVisitor.Replace(where.Parameters[0], row, where.Body));
            step = call.Arguments[0];
        }

        return (true, conditions.Count == 0 ? null : Expression.Lambda(conditions.Aggregate(Expression.AndAlso), row));
    }

    private static Type? ElementType(Type type) =>
        type == typeof(string) ? null
        : type.GetInterfaces().Append(type).FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))?.GenericTypeArguments[0];

    private sealed class Rewriter(ODataNavigationScope scope) : ExpressionVisitor
    {
        private readonly HashSet<Expression> _nulled = [];

        public override Expression? Visit(Expression? node) => node is not null && Roots.TryGetValue(node, out _) ? node : base.Visit(node);

        // Include(x => x.Nav) names a navigation, it does not read one
        protected override Expression VisitMethodCall(MethodCallExpression node) =>
            node.Method.DeclaringType == typeof(EntityFrameworkQueryableExtensions) && node.Method.Name.Contains("Include", StringComparison.Ordinal)
                ? node.Update(node.Object, [Visit(node.Arguments[0])!, .. node.Arguments.Skip(1)])
                : base.VisitMethodCall(node);

        protected override Expression VisitMember(MemberExpression node)
        {
            var visited = Pushed((MemberExpression)base.VisitMember(node));
            if (node.Expression is null || !scope._navigations.Contains((node.Member.DeclaringType, node.Member.Name)))
            {
                return visited;
            }

            if (scope._predicates.TryGetValue(node.Type, out var single))
            {
                return Nulled(ReplacingExpressionVisitor.Replace(single.Parameters[0], visited, single.Body), visited);
            }

            return ElementType(node.Type) is { } element && scope._predicates.TryGetValue(element, out var many)
                ? Expression.Call(typeof(Enumerable), nameof(Enumerable.Where), [element], visited, many)
                : visited;
        }

        // (shown ? x.Nav : null).Name cannot be translated, shown ? x.Nav.Name : null can
        private Expression Pushed(MemberExpression member) => Into(member, member.Expression);

        private Expression Into(MemberExpression member, Expression? target) =>
            target is ConditionalExpression conditional && _nulled.Contains(conditional)
                ? Nulled(conditional.Test, Into(member, conditional.IfTrue))
                : member.Update(target);

        private Expression Nulled(Expression shown, Expression value)
        {
            if (!value.Type.IsValueType || Nullable.GetUnderlyingType(value.Type) is not null)
            {
                var conditional = Expression.Condition(shown, value, Expression.Constant(null, value.Type));
                _nulled.Add(conditional);
                return conditional;
            }

            // a value type keeps its type: (int)(shown ? (int?)x.Nav.Size : null) is NULL in SQL
            var nullable = typeof(Nullable<>).MakeGenericType(value.Type);
            return Expression.Convert(Expression.Condition(shown, Expression.Convert(value, nullable), Expression.Constant(null, nullable)), value.Type);
        }
    }
}
