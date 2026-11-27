using System.Linq.Expressions;
using Coworkee.Application.Authorization;
using Coworkee.Application.Groups;
using Coworkee.Contracts.DataFilters;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.OData.DataFilters;

/// <summary>
/// The hidden data filters of an entity set for the current user, as one predicate: System AND Tenant AND each active role AND the group part. The group part
/// only narrows, it never scopes: a row that comes through none of the user's active groups is left to the app's own scope; one that comes through an active
/// group without filters (own and ancestors') passes; one that comes only through filtered active groups must pass the filters of one of them. For sets
/// without an <see cref="IGroupRowSource{TEntity}"/> a row passes when one active group's filters let it through. A linked set's rows also need their target row
/// to pass the target's data filters.
/// </summary>
public sealed class DataFilterResolver(IServiceProvider services, ICurrentUser user, IPermissionChecker permissions, ODataEntityRegistry registry)
{
    private readonly DataFilterCache _cache = services.GetRequiredService<DataFilterCache>();

    // once per user, set and group scope switch in a request: a request reads a set several times (list, count, facets)
    private readonly Dictionary<(Guid?, Guid?, string, Type, bool, bool, IReadOnlyList<Guid>?), LambdaExpression?> _resolved = [];

    // once per organisation in a scope: inside a transaction the cache does not keep them, and every set would read them again
    private readonly Dictionary<Guid, DataFilterRows> _rows = [];

    private readonly Dictionary<Guid, bool> _systemTenant = [];

    /// <summary>
    /// The rows of <paramref name="entitySet"/> the user may see, <c>null</c> when no filter restricts him (none stored, or he holds
    /// <see cref="DataFilterPermissions.Bypass"/> in the system organisation; elsewhere Bypass skips every layer but System, as every organisation's
    /// admin holds it). <paramref name="byKey"/>: a read of single records, which skips filters that do not apply to details.
    /// </summary>
    public async Task<Expression<Func<TEntity, bool>>?> PredicateAsync<TEntity>(string entitySet, bool byKey, CancellationToken ct)
    {
        var key = (user.UserId, user.TenantId, entitySet, typeof(TEntity), byKey, GroupScope.IsUnnarrowed, GroupScope.SelectionOverride);
        if (!_resolved.TryGetValue(key, out var predicate))
        {
            _resolved[key] = predicate = await ResolveAsync<TEntity>(entitySet, byKey, ct);
        }

        return (Expression<Func<TEntity, bool>>?)predicate;
    }

    private async Task<Expression<Func<TEntity, bool>>?> ResolveAsync<TEntity>(string entitySet, bool byKey, CancellationToken ct)
    {
        if (!_rows.TryGetValue(user.TenantId ?? Guid.Empty, out var stored))
        {
            _rows[user.TenantId ?? Guid.Empty] = stored = await _cache.FiltersAsync(services, user.TenantId, ct);
        }

        var filters = stored.Filters
            .Where(f => string.Equals(f.EntitySet, entitySet, StringComparison.OrdinalIgnoreCase) && (!byKey || f.AppliesToDetails))
            .ToList();
        var link = LinkOf(stored.Links, entitySet);
        if (filters.Count == 0 && link is null)
        {
            return null;
        }

        // the Admin role of every organisation holds Bypass: it skips the organisation's own layers there, System only in the system organisation
        if (await permissions.IsGrantedAsync(DataFilterPermissions.Bypass, ct))
        {
            if (await IsSystemTenantAsync(ct))
            {
                return null;
            }

            filters.RemoveAll(f => f.Layer != DataFilterLayer.System);
            if (filters.Count == 0 && link is null)
            {
                return null;
            }
        }

        var entity = registry.Find(entitySet);
        var now = DateTimeOffset.Now;
        Expression<Func<TEntity, bool>>? Of(DataFilterRow filter) => _cache.Predicate<TEntity>(services, entity, filter, now);

        var parts = filters.Where(f => f.Layer is DataFilterLayer.System or DataFilterLayer.Tenant).Select(Of).ToList();
        if (filters.Any(f => f.Layer == DataFilterLayer.Role))
        {
            var roles = await permissions.GetRoleIdsAsync(ct);
            parts.AddRange(filters.Where(f => f.Layer == DataFilterLayer.Role && f.LayerKey is { } role && roles.Contains(role)).Select(Of));
        }

        var groupFilters = filters.Where(f => f.Layer == DataFilterLayer.Group && f.LayerKey is not null).ToList();
        if (groupFilters.Count > 0)
        {
            parts.Add(await GroupPartAsync(groupFilters, Of, ct));
        }

        if (link is not null)
        {
            parts.Add(await LinkedAsync<TEntity>(link, stored.Links, byKey, ct));
        }

        return All(parts);
    }

    private async Task<bool> IsSystemTenantAsync(CancellationToken ct)
    {
        if (user.TenantId is not { } tenantId || services.GetService<ITenantDirectory>() is not { } tenants)
        {
            return false;
        }

        if (!_systemTenant.TryGetValue(tenantId, out var system))
        {
            _systemTenant[tenantId] = system = await tenants.IsSystemTenantAsync(tenantId, ct);
        }

        return system;
    }

    private static DataFilterLinkRow? LinkOf(IEnumerable<DataFilterLinkRow> links, string entitySet) =>
        links.FirstOrDefault(l => string.Equals(l.EntitySet, entitySet, StringComparison.OrdinalIgnoreCase));

    private static readonly System.Reflection.MethodInfo LinkMethod =
        typeof(DataFilterResolver).GetMethod(nameof(LinkAsync), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

    /// <summary>
    /// A linked set: its rows whose <see cref="DataFilterLinkRow.Property"/> names a row of the target set that passes the target's data filters (links of the
    /// target included). A broken link (missing target, a chain that comes back to itself) hides every row.
    /// </summary>
    private async Task<Expression<Func<TEntity, bool>>?> LinkedAsync<TEntity>(DataFilterLinkRow link, IReadOnlyList<DataFilterLinkRow> links, bool byKey, CancellationToken ct)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { link.EntitySet };
        for (var next = link; next is not null; next = LinkOf(links, next.TargetSet))
        {
            if (!seen.Add(next.TargetSet))
            {
                return _ => false;
            }
        }

        return registry.Find(link.TargetSet) is { } target
            ? (Expression<Func<TEntity, bool>>?)await (Task<LambdaExpression?>)LinkMethod.MakeGenericMethod(typeof(TEntity), target.EntityType).Invoke(this, [link, target, byKey, ct])!
            : _ => false;
    }

    // e => target.Where(passes).Any(t => t.Key == e.Property), null when no filter restricts the target
    private async Task<LambdaExpression?> LinkAsync<TEntity, TTarget>(DataFilterLinkRow link, ODataEntityRegistration target, bool byKey, CancellationToken ct)
        where TTarget : class
    {
        if (await PredicateAsync<TTarget>(target.EntitySet, byKey, ct) is not { } passes)
        {
            return null;
        }

        var db = services.DbContextFor(typeof(TEntity));
        var key = db.Model.FindEntityType(typeof(TTarget))?.FindPrimaryKey()?.Properties is [{ PropertyInfo: { } single }] ? single : null;
        var property = DataFilterProperties.Find(typeof(TEntity), link.Property);
        if (db != services.DbContextFor(typeof(TTarget)) || key is null || property is null
            || (Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType) != (Nullable.GetUnderlyingType(key.PropertyType) ?? key.PropertyType))
        {
            return (Expression<Func<TEntity, bool>>)(_ => false);
        }

        // the target row passes or not, deleted or not: the linked row's own soft delete decides about it
        var rows = db.Set<TTarget>().IgnoreQueryFilters([CoworkeeDbContext.SoftDeleteFilter]).Where(passes);
        var (e, t) = (Expression.Parameter(typeof(TEntity), "e"), Expression.Parameter(typeof(TTarget), "t"));
        Expression targetKey = Expression.Property(t, key), value = Expression.Property(e, property);
        if (targetKey.Type != value.Type)
        {
            // Guid against Guid?: the plain side becomes nullable
            if (Nullable.GetUnderlyingType(value.Type) is null)
            {
                value = Expression.Convert(value, targetKey.Type);
            }
            else
            {
                targetKey = Expression.Convert(targetKey, value.Type);
            }
        }

        var same = Expression.Lambda<Func<TTarget, bool>>(Expression.Equal(targetKey, value), t);
        return Expression.Lambda<Func<TEntity, bool>>(Expression.Call(typeof(Queryable), nameof(Queryable.Any), [typeof(TTarget)], rows.Expression, Expression.Quote(same)), e);
    }

    private async Task<Expression<Func<TEntity, bool>>?> GroupPartAsync<TEntity>(
        List<DataFilterRow> groupFilters, Func<DataFilterRow, Expression<Func<TEntity, bool>>?> of, CancellationToken ct)
    {
        var source = services.GetService<IGroupRowSource<TEntity>>();
        if (services.GetService<IGroupScope>() is not { } scope
            || await permissions.IsGrantedAsync(IdentityPermissions.Groups.ScopeAll, ct)
            || (source is not null && await source.SeesAllAsync(ct)))
        {
            return null;
        }

        // the groups the user works in now: his selection with its sub-groups, else all his groups with their sub-groups
        var active = (await scope.ActiveAsync(ct)).Distinct().ToList();
        if (active.Count == 0)
        {
            return null;
        }

        var codes = (await scope.NodesAsync(active.Concat(groupFilters.Select(f => f.LayerKey!.Value)), ct)).ToDictionary(n => n.Id, n => n.Code);
        string? CodeOf(Guid id) => codes.GetValueOrDefault(id) is { Length: > 0 } code ? code : null;

        // a group's filters and those of its ancestors (whose codes lead its own), grouped by the same set of filters: one term per set, not per group
        var unfiltered = new List<Guid>();
        var filtered = new List<(List<Guid> Groups, Expression<Func<TEntity, bool>> Filter)>();
        foreach (var same in active.GroupBy(group => string.Join(',', groupFilters.Where(f => Inherits(group, f.LayerKey!.Value)).Select(f => f.Id).Order())))
        {
            var inherited = groupFilters.Where(f => Inherits(same.First(), f.LayerKey!.Value)).Select(of).ToList();
            if (inherited.Count == 0 || All(inherited) is not { } filter)
            {
                unfiltered.AddRange(same);
            }
            else
            {
                filtered.Add((same.ToList(), filter));
            }
        }

        if (filtered.Count == 0)
        {
            return null;
        }

        if (source is null)
        {
            return unfiltered.Count > 0 ? null : filtered.Select(f => f.Filter).Aggregate(Or);
        }

        // NOT rowsOf(all active) OR rowsOf(unfiltered) OR rowsOf(G1) AND f1 OR ...: joining a filtered group never hides what another group or the app shows
        var terms = new List<Expression<Func<TEntity, bool>>> { Not(await source.RowsOfAsync(active, ct)) };
        if (unfiltered.Count > 0)
        {
            terms.Add(await source.RowsOfAsync(unfiltered, ct));
        }

        foreach (var (groups, filter) in filtered)
        {
            terms.Add(And(await source.RowsOfAsync(groups, ct), filter));
        }

        return terms.Aggregate(Or);

        bool Inherits(Guid group, Guid filteredGroup) =>
            group == filteredGroup || (CodeOf(group) is { } code && CodeOf(filteredGroup) is { } ancestor && code.StartsWith(ancestor + ".", StringComparison.Ordinal));
    }

    /// <summary>All parts AND-ed, <c>null</c> (no restriction) for parts that let every row through.</summary>
    private static Expression<Func<TEntity, bool>>? All<TEntity>(IEnumerable<Expression<Func<TEntity, bool>>?> parts) =>
        parts.OfType<Expression<Func<TEntity, bool>>>().Aggregate((Expression<Func<TEntity, bool>>?)null, (all, part) => all is null ? part : And(all, part));

    private static Expression<Func<TEntity, bool>> And<TEntity>(Expression<Func<TEntity, bool>> a, Expression<Func<TEntity, bool>> b) => Combine(a, b, ExpressionType.AndAlso);

    private static Expression<Func<TEntity, bool>> Or<TEntity>(Expression<Func<TEntity, bool>> a, Expression<Func<TEntity, bool>> b) => Combine(a, b, ExpressionType.OrElse);

    private static Expression<Func<TEntity, bool>> Not<TEntity>(Expression<Func<TEntity, bool>> a) => Expression.Lambda<Func<TEntity, bool>>(Expression.Not(a.Body), a.Parameters);

    private static Expression<Func<TEntity, bool>> Combine<TEntity>(Expression<Func<TEntity, bool>> a, Expression<Func<TEntity, bool>> b, ExpressionType type) =>
        Expression.Lambda<Func<TEntity, bool>>(Expression.MakeBinary(type, a.Body, ReplacingExpressionVisitor.Replace(b.Parameters[0], a.Parameters[0], b.Body)), a.Parameters);
}
