using System.Collections.Concurrent;
using System.Linq.Expressions;
using Coworkee.Application.Caching;
using Coworkee.Contracts.DataFilters;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Coworkee.OData.DataFilters;

/// <summary>A stored data filter as the resolver reads it.</summary>
internal sealed record DataFilterRow(Guid Id, string EntitySet, DataFilterLayer Layer, Guid? LayerKey, string Query, bool AppliesToDetails, int Version);

/// <summary>
/// The data filters of an organisation (its own and the System ones), shared by all requests until a filter changes, and every filter parsed once per
/// version and day (relative dates such as "in the last 7 days" move with the day).
/// </summary>
internal sealed partial class DataFilterCache(HybridCache cache, IMemoryCache parsed, ILogger<DataFilterCache> logger)
{
    internal const string Tag = "coworkee:data-filters";

    private static readonly TimeSpan ParsedLifetime = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<(Guid, int), bool> _warned = new();

    /// <summary>The System filters and those of <paramref name="tenantId"/>; filters of other organisations never, whatever their layer key names.</summary>
    public async Task<IReadOnlyList<DataFilterRow>> FiltersAsync(IServiceProvider services, Guid? tenantId, CancellationToken ct) =>
        await cache.GetOrCreateInScopeAsync(
            services,
            new ImpersonatedUser(null, tenantId),
            $"{Tag}:{tenantId}",
            (scope, token) => scope.GetRequiredService<CoworkeeDbContext>().Set<DataFilter>().AsNoTracking()
                .Where(f => f.TenantId == null ? f.Layer == DataFilterLayer.System : f.TenantId == tenantId && f.Layer != DataFilterLayer.System)
                .Select(f => new DataFilterRow(f.Id, f.EntitySet, f.Layer, f.LayerKey, f.Query, f.AppliesToDetails, f.Version))
                .ToListAsync(token),
            tags: [Tag],
            cancellationToken: ct);

    /// <summary>
    /// The rows <paramref name="filter"/> lets through, <c>null</c> when it lets every row through (no complete rule); <c>false</c> for every row when it no longer
    /// reads against the model of <paramref name="entity"/> (or there is no such set): a broken filter hides, it never shows more.
    /// </summary>
    public Expression<Func<TEntity, bool>>? Predicate<TEntity>(IServiceProvider services, ODataEntityRegistration? entity, DataFilterRow filter, DateTimeOffset now) =>
        parsed.GetOrCreate((Tag, typeof(TEntity), filter.EntitySet, filter.Id, filter.Version, now.Date), entry =>
        {
            entry.SlidingExpiration = ParsedLifetime;
            return Parse<TEntity>(services, entity, filter, now);
        });

    private Expression<Func<TEntity, bool>>? Parse<TEntity>(IServiceProvider services, ODataEntityRegistration? entity, DataFilterRow filter, DateTimeOffset now)
    {
        string? problem;
        try
        {
            FilterQueryOption? option;
            (option, problem) = entity is null ? (null, $"There is no entity set '{filter.EntitySet}'.") : DataFilterQueries.Parse(services, entity, filter.Query, now);
            if (problem is null)
            {
                if (option is null)
                {
                    return null;
                }

                // what EF gets: no null checks of its own, values as parameters
                var where = (MethodCallExpression)option.ApplyTo(Enumerable.Empty<TEntity>().AsQueryable(), new ODataQuerySettings { HandleNullPropagation = HandleNullPropagationOption.False }).Expression;
                return (Expression<Func<TEntity, bool>>)((UnaryExpression)where.Arguments[1]).Operand;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            problem = exception.Message;
        }

        if (_warned.TryAdd((filter.Id, filter.Version), true))
        {
            LogBroken(filter.Id, filter.Version, filter.EntitySet, problem);
        }

        return _ => false;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The data filter {FilterId} (version {Version}) of {EntitySet} does not apply and hides every row: {Problem}")]
    private partial void LogBroken(Guid filterId, int version, string entitySet, string problem);
}

/// <summary>Drops the cached filters of every organisation once a data filter is saved or deleted (on every replica through the cache's tags).</summary>
internal sealed class DataFilterCacheInterceptor(HybridCache cache) : SaveChangesInterceptor
{
    private bool _pending;

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Detect(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Detect(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (_pending)
        {
            _pending = false;
            await cache.RemoveByTagAsync(DataFilterCache.Tag, cancellationToken);
        }

        return result;
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (_pending)
        {
            _pending = false;
            cache.RemoveByTagAsync(DataFilterCache.Tag).AsTask().GetAwaiter().GetResult();
        }

        return result;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => _pending = false;

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        _pending = false;
        return Task.CompletedTask;
    }

    private void Detect(DbContext? context) =>
        _pending |= context?.ChangeTracker.Entries<DataFilter>().Any(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted) == true;
}
