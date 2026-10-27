using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Coworkee.OData;

/// <summary>
/// Row filters and set permissions for navigations: a query over a set reaches other rows through its navigations ($expand, $filter with
/// any/all, $orderby on a path, report fields, search fields), and <see cref="ApplyAsync{T}"/> makes each of them show only what the target's own
/// set lists for the user. A collection gets <c>.Where(filters)</c>, a reference becomes null when its row is hidden. A target set the user
/// may not read shows nothing, and so does a target whose filters do more than <c>Where</c> or fail (they cannot be applied inside another query).
/// Only the targets a query reaches are checked; their decisions are kept for the lifetime of the scope (one request).
/// </summary>
public sealed class ODataNavigationScope
{
    // queries VisibleAsync built or Apply rewrote: their row filters are applied already, the rewrite leaves them alone
    private static readonly ConditionalWeakTable<Expression, object> Roots = new();

    private static readonly MethodInfo HarvestMethod = typeof(ODataNavigationScope).GetMethod(nameof(HarvestAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    private readonly IServiceProvider _services;
    private readonly Dictionary<Type, IEntityType> _types = [];
    private readonly HashSet<(Type?, string)> _navigations = [];
    private readonly Dictionary<(Type?, string), PropertyInfo> _inverses = [];

    // the rules of each target checked so far (null: none), and what the rewrite applies (a base type adds the rules of its derived types)
    private readonly Dictionary<Type, LambdaExpression?> _own = [];
    private readonly Dictionary<Type, LambdaExpression> _predicates = [];

    private ODataNavigationScope(IServiceProvider services) => _services = services;

    /// <summary>Navigation targets whose set the user may not read.</summary>
    internal HashSet<Type> Forbidden { get; } = [];

    /// <summary>Navigation targets whose row filters do more than <c>Where</c>, or fail when read.</summary>
    internal HashSet<Type> Unfilterable { get; } = [];

    /// <summary>The navigations reachable from <paramref name="root"/> in the EF model; their targets are checked when a query reaches them.</summary>
    public static ODataNavigationScope For(IServiceProvider services, Type root)
    {
        var scope = new ODataNavigationScope(services);
        scope.Walk(root);
        return scope;
    }

    /// <summary>The query with every navigation in it (outside the visible rows it started from) narrowed to the rows its target shows.</summary>
    public async Task<IQueryable<T>> ApplyAsync<T>(IQueryable<T> query, CancellationToken cancellationToken) =>
        (IQueryable<T>)await ApplyAsync((IQueryable)query, cancellationToken);

    /// <inheritdoc cref="ApplyAsync{T}"/>
    public async Task<IQueryable> ApplyAsync(IQueryable query, CancellationToken cancellationToken)
    {
        await PrepareAsync(query.Expression, cancellationToken);
        return Apply(query);
    }

    /// <summary>Checks the targets of the navigations <paramref name="expression"/> reads, so <see cref="Apply"/> can rewrite it without waiting.</summary>
    internal Task PrepareAsync(Expression expression, CancellationToken cancellationToken)
    {
        var collector = new Collector(this);
        collector.Visit(expression);
        return PrepareAsync(collector.Targets, cancellationToken);
    }

    /// <summary>Whether <paramref name="expression"/> reads a navigation of the scope.</summary>
    internal bool Reaches(Expression expression)
    {
        var collector = new Collector(this);
        collector.Visit(expression);
        return collector.Targets.Count > 0;
    }

    /// <summary>Checks every reachable target, for a query that could not be read ahead.</summary>
    internal Task PrepareAllAsync(CancellationToken cancellationToken) => PrepareAsync(_types.Keys.ToList(), cancellationToken);

    /// <summary>The query rewritten with the targets checked before (<see cref="PrepareAsync(Expression, CancellationToken)"/>).</summary>
    internal IQueryable Apply(IQueryable query)
    {
        if (_navigations.Count == 0)
        {
            return query;
        }

        var rewritten = query.Provider.CreateQuery(new Rewriter(this).Visit(query.Expression)!);
        Root(rewritten.Expression);
        return rewritten;
    }

    /// <summary>Marks the rows of a query as visible already: <see cref="Apply"/> does not touch navigations inside it (the set's own row filters).</summary>
    internal static void Root(Expression expression) => Roots.AddOrUpdate(expression, true);

    private async Task PrepareAsync(IEnumerable<Type> targets, CancellationToken cancellationToken)
    {
        // rows of derived types come through a navigation to their base
        var types = targets.Where(_types.ContainsKey).SelectMany(t => _types[t].GetDerivedTypesInclusive()).Select(t => t.ClrType)
            .Distinct().Where(t => !_own.ContainsKey(t)).ToList();
        if (types.Count == 0)
        {
            return;
        }

        var registry = _services.GetService<ODataEntityRegistry>();
        foreach (var type in types)
        {
            if (registry?.Find(type) is not null && !await ODataEntityAccess.IsGrantedAsync(_services, type, cancellationToken))
            {
                Forbidden.Add(type);
                _own[type] = Nothing(type);
                continue;
            }

            (bool Harvested, LambdaExpression? Predicate) harvest;
            try
            {
                harvest = await (Task<(bool, LambdaExpression?)>)HarvestMethod.MakeGenericMethod(type).Invoke(null, [_services, cancellationToken])!;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // fails closed for this type only, the rest of the query works
                _services.GetService<ILoggerFactory>()?.CreateLogger<ODataNavigationScope>()
                    .LogWarning(exception, "The row filters of {Type} failed outside its own set; behind navigations its rows stay hidden.", type.Name);
                harvest = (false, null);
            }

            if (!harvest.Harvested)
            {
                Unfilterable.Add(type);
            }

            _own[type] = harvest.Harvested ? harvest.Predicate : Nothing(type);
        }

        Combine();
    }

    /// <summary>
    /// A navigation to a base type (Invoice.Shipment of ShipmentBase) also reads rows of the derived sets: each derived type with rules adds
    /// <c>!(x is D) || rules((D)x)</c> to the base, so a shipment hidden in its own set is hidden there too.
    /// </summary>
    private void Combine()
    {
        _predicates.Clear();
        foreach (var type in _own.Keys)
        {
            var row = Expression.Parameter(type, "x");
            var conditions = _types.TryGetValue(type, out var entity)
                ? entity.GetDerivedTypes()
                    .Where(d => _own.GetValueOrDefault(d.ClrType) is not null)
                    .Select(d => (Expression)Expression.OrElse(Expression.Not(Expression.TypeIs(row, d.ClrType)), Body(_own[d.ClrType]!, Expression.Convert(row, d.ClrType))))
                    .ToList()
                : [];
            if (_own[type] is { } own)
            {
                conditions.Insert(0, Body(own, row));
            }

            if (conditions.Count > 0)
            {
                _predicates[type] = Expression.Lambda(conditions.Aggregate(Expression.AndAlso), row);
            }
        }
    }

    private void Walk(Type root)
    {
        if (_services.DbContextFor(root).Model.FindEntityType(root) is not { } start)
        {
            return;
        }

        var seen = new HashSet<IEntityType>();
        var queue = new Queue<IEntityType>([start]);
        while (queue.TryDequeue(out var type))
        {
            foreach (var navigation in type.GetNavigations().Cast<INavigationBase>().Concat(type.GetSkipNavigations()))
            {
                if (navigation.PropertyInfo is { } property)
                {
                    _navigations.Add((property.DeclaringType, property.Name));
                    if (navigation.Inverse?.PropertyInfo is { } inverse)
                    {
                        _inverses[(property.DeclaringType, property.Name)] = inverse;
                    }
                }

                // derived types too: their rows come through a navigation to the base, their own navigations through a cast
                foreach (var target in navigation.TargetEntityType.GetDerivedTypesInclusive().Where(seen.Add))
                {
                    queue.Enqueue(target);
                }
            }
        }

        foreach (var type in seen.Where(t => !t.HasSharedClrType))
        {
            _types.TryAdd(type.ClrType, type);
        }
    }

    // the target type of a navigation member, or null when the member is no navigation of the scope
    private Type? Target(MemberExpression node) =>
        node.Expression is not null && _navigations.Contains((node.Member.DeclaringType, node.Member.Name)) ? ElementType(node.Type) ?? node.Type : null;

    // a target the scope has not checked (a query applied without ApplyAsync) fails instead of showing unfiltered rows
    private LambdaExpression? Predicate(Type type) =>
        !_types.ContainsKey(type) || _own.ContainsKey(type)
            ? _predicates.GetValueOrDefault(type)
            : throw new InvalidOperationException($"The navigation scope has not checked {type.Name}; apply it with ApplyAsync.");

    private static Expression Body(LambdaExpression predicate, Expression row) => ReplacingExpressionVisitor.Replace(predicate.Parameters[0], row, predicate.Body);

    private static Expression? And(Expression? left, Expression? right) => left is null ? right : right is null ? left : Expression.AndAlso(left, right);

    private static LambdaExpression Nothing(Type type) => Expression.Lambda(Expression.Constant(false), Expression.Parameter(type, "x"));

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

            conditions.Add(Body(where, row));
            step = call.Arguments[0];
        }

        return (true, conditions.Count == 0 ? null : Expression.Lambda(conditions.Aggregate(Expression.AndAlso), row));
    }

    private static Type? ElementType(Type type) =>
        type == typeof(string) ? null
        : type.GetInterfaces().Append(type).FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))?.GenericTypeArguments[0];

    // Include(x => x.Nav) names a navigation, it does not read one
    private static bool IsInclude(MethodCallExpression node) =>
        node.Method.DeclaringType == typeof(EntityFrameworkQueryableExtensions) && node.Method.Name.Contains("Include", StringComparison.Ordinal);

    /// <summary>The targets of the navigations a query reads, outside its roots and <c>Include</c>s.</summary>
    private sealed class Collector(ODataNavigationScope scope) : ExpressionVisitor
    {
        public HashSet<Type> Targets { get; } = [];

        public override Expression? Visit(Expression? node) => node is not null && Roots.TryGetValue(node, out _) ? node : base.Visit(node);

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (!IsInclude(node))
            {
                return base.VisitMethodCall(node);
            }

            Visit(node.Arguments[0]);
            return node;
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            if (scope.Target(node) is { } target)
            {
                Targets.Add(target);
            }

            return base.VisitMember(node);
        }
    }

    private sealed class Rewriter(ODataNavigationScope scope) : ExpressionVisitor
    {
        private readonly HashSet<Expression> _nulled = [];

        public override Expression? Visit(Expression? node) => node is not null && Roots.TryGetValue(node, out _) ? node : base.Visit(node);

        protected override Expression VisitMethodCall(MethodCallExpression node) =>
            IsInclude(node) ? node.Update(node.Object, [Visit(node.Arguments[0])!, .. node.Arguments.Skip(1)]) : base.VisitMethodCall(node);

        protected override Expression VisitMember(MemberExpression node)
        {
            var parent = Visit(node.Expression);
            if (scope.Target(node) is not { } target)
            {
                return Into(node, parent);
            }

            // a navigation is read from the plain parent and a hidden parent's test joins its own: (shown(x.A) && shown(x.A.B)) ? x.A.B : null
            var (test, source) = parent is ConditionalExpression conditional && _nulled.Contains(conditional) ? (conditional.Test, conditional.IfTrue) : (null, parent);
            var plain = node.Update(source);
            var predicate = scope.Predicate(target);
            if (ElementType(node.Type) is { } element)
            {
                // a collection under a hidden reference is empty: x.A.Items.Where(i => shown(x.A) && shown(i)); through the inverse navigation
                // the test reads the item's own parent, x.A.Items.Where(i => shown(i.A) && ...), which needs no lateral join (SQLite has none)
                var item = Expression.Parameter(element, "e");
                if (test is not null && scope._inverses.GetValueOrDefault((node.Member.DeclaringType, node.Member.Name)) is { } inverse && inverse.PropertyType == source!.Type)
                {
                    test = ReplacingExpressionVisitor.Replace(source, Expression.Property(item, inverse), test);
                }

                return And(test, predicate is null ? null : Body(predicate, item)) is { } keep
                    ? Expression.Call(typeof(Enumerable), nameof(Enumerable.Where), [element], plain, Expression.Lambda(keep, item))
                    : plain;
            }

            return And(test, predicate is null ? null : Body(predicate, plain)) is { } shown ? Nulled(shown, plain) : plain;
        }

        // (shown ? x.Nav : null).Name cannot be translated, shown ? x.Nav.Name : null can
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
