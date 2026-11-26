using System.Linq.Expressions;
using Coworkee.Application.Authorization;
using Coworkee.Application.Groups;
using Coworkee.Contracts.DataFilters;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Security;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.OData.DataFilters;

/// <summary>
/// The hidden data filters of an entity set for the current user, as one predicate: System AND Tenant AND each active role AND the group part. The group part
/// only narrows, it never scopes: a row that comes through none of the user's active groups is left to the app's own scope; one that comes through an active
/// group without filters (own and ancestors') passes; one that comes only through filtered active groups must pass the filters of one of them. For sets
/// without an <see cref="IGroupRowSource{TEntity}"/> a row passes when one active group's filters let it through.
/// </summary>
public sealed class DataFilterResolver(IServiceProvider services, ICurrentUser user, IPermissionChecker permissions, ODataEntityRegistry registry)
{
    private readonly DataFilterCache _cache = services.GetRequiredService<DataFilterCache>();

    // once per user, set and group scope switch in a request: a request reads a set several times (list, count, facets)
    private readonly Dictionary<(Guid?, Guid?, string, Type, bool, bool, IReadOnlyList<Guid>?), LambdaExpression?> _resolved = [];

    /// <summary>
    /// The rows of <paramref name="entitySet"/> the user may see, <c>null</c> when no filter restricts him (none stored, or he holds
    /// <see cref="DataFilterPermissions.Bypass"/>). <paramref name="byKey"/>: a read of single records, which skips filters that do not apply to details.
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
        var filters = (await _cache.FiltersAsync(services, user.TenantId, ct))
            .Where(f => string.Equals(f.EntitySet, entitySet, StringComparison.OrdinalIgnoreCase) && (!byKey || f.AppliesToDetails))
            .ToList();
        if (filters.Count == 0 || await permissions.IsGrantedAsync(DataFilterPermissions.Bypass, ct))
        {
            return null;
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

        return All(parts);
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
