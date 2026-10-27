using System.Diagnostics;
using System.Reflection;
using Coworkee.Application.Authorization;
using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Reporting;
using Coworkee.Core.Results;
using Coworkee.Infrastructure.Persistence;
using Coworkee.OData;
using Microsoft.OData;

namespace Coworkee.Reporting.Querying;

/// <summary>Sources the user may read and aggregates over them, with the entity set's permission, tenant and row filters.</summary>
internal sealed class ReportQueries(IServiceProvider services, ReportSchema schema, IPermissionChecker permissions)
{
    private static readonly MethodInfo RunMethod = typeof(ReportQueries).GetMethod(nameof(RunTypedAsync), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo CheckMethod = typeof(ReportQueries).GetMethod(nameof(CheckFilterAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    private IReadOnlySet<string>? _granted;

    public async Task<IReadOnlyList<ReportSource>> SourcesAsync(CancellationToken cancellationToken)
    {
        var granted = await GrantedAsync(cancellationToken);
        return schema.All
            .Where(s => s.Permission is null || granted.Contains(s.Permission))
            .Select(s => s with { Fields = [.. s.Fields.Where(f => f.Allowed(granted))] })
            .ToList();
    }

    public async Task<Result<ReportQueryResult>> RunAsync(ReportQueryRequest request, CancellationToken cancellationToken)
    {
        var (source, plan) = await PlanAsync(request, cancellationToken);
        if (plan is { IsSuccess: false })
        {
            return plan.Error;
        }

        var watch = Stopwatch.StartNew();
        try
        {
            var rows = await (Task<Result<IReadOnlyList<ReportQueryRow>>>)RunMethod.MakeGenericMethod(source!.RowType)
                .Invoke(null, [services, source, request.Filter, plan.Value, cancellationToken])!;
            return rows.IsSuccess ? new ReportQueryResult(rows.Value, watch.ElapsedMilliseconds) : rows.Error;
        }
        catch (ODataException exception)
        {
            return Error.Validation(nameof(ReportQueryRequest.Filter), exception.Message);
        }
    }

    /// <summary>Checks a query without running it: the source, its fields and the filter.</summary>
    public async Task<Error?> CheckAsync(ReportQueryRequest request, CancellationToken cancellationToken)
    {
        var (source, plan) = await PlanAsync(request, cancellationToken);
        if (!plan.IsSuccess)
        {
            return plan.Error;
        }

        try
        {
            if (source!.Computed is { } computed)
            {
                computed.Parse(request.Filter);
            }
            else
            {
                await (Task)CheckMethod.MakeGenericMethod(source.RowType).Invoke(null, [services, request.Filter, cancellationToken])!;
            }

            return null;
        }
        catch (ODataException exception)
        {
            return Error.Validation(nameof(ReportQueryRequest.Filter), exception.Message);
        }
    }

    private async Task<(ReportSource? Source, Result<ReportPlan> Plan)> PlanAsync(ReportQueryRequest request, CancellationToken cancellationToken)
    {
        var source = await ReadableAsync(request.EntitySet, cancellationToken);
        if (!source.IsSuccess)
        {
            return (null, source.Error);
        }

        // an app source aggregates in memory unless it hands out a query of the database
        var provider = source.Value.Computed is null ? CoworkeeDatabase.Provider(services.DbContextFor(source.Value.RowType).Database) : DatabaseProviders.Postgres;
        var model = source.Value.Computed is null ? services.DbContextFor(source.Value.RowType).Model : null;
        return (source.Value, ReportPlan.Create(source.Value, request, await GrantedAsync(cancellationToken), provider,
            model is null ? null : field => StoredAsNumber(model, source.Value.RowType, field.Chain)));
    }

    internal async Task<Result<ReportSource>> ReadableAsync(string name, CancellationToken cancellationToken)
    {
        if (schema.Find(name) is not { } source)
        {
            return Error.NotFound("reports.unknown_set", $"There is no data set '{name}'.");
        }

        return source.Permission is { } permission && !(await GrantedAsync(cancellationToken)).Contains(permission)
            ? Error.Forbidden("reports.forbidden_set", $"You may not read {source.EntitySet}.")
            : source;
    }

    // the column of a field, through its navigations, keeps a DateTimeOffset as DateTimeOffsetToBinaryConverter writes it, and the context maps the
    // function the buckets read it with (a Coworkee DbContext does, through ReportModelContributor; an external one does not)
    internal static bool StoredAsNumber(Microsoft.EntityFrameworkCore.Metadata.IModel model, Type rowType, IReadOnlyList<System.Reflection.PropertyInfo> chain)
    {
        if (Microsoft.EntityFrameworkCore.RelationalModelExtensions.FindDbFunction(model, typeof(DateBuckets).GetMethod(nameof(DateBuckets.Stored))!) is null)
        {
            return false;
        }

        var entity = model.FindEntityType(rowType);
        foreach (var step in chain.Take(chain.Count - 1))
        {
            entity = entity?.FindNavigation(step.Name)?.TargetEntityType;
        }

        return entity?.FindProperty(chain[^1].Name)?.GetValueConverter() is Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter;
    }

    internal async Task<IReadOnlySet<string>> GrantedAsync(CancellationToken cancellationToken) =>
        _granted ??= (await permissions.GetGrantedAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);

    // one navigation scope for the filter and the grouped query: each target set is checked once
    private static async Task<Result<IReadOnlyList<ReportQueryRow>>> RunTypedAsync<TEntity>(IServiceProvider services, ReportSource source, string? filter, ReportPlan plan, CancellationToken cancellationToken)
        where TEntity : class
    {
        if (source.Computed is { } computed)
        {
            return await ReportAggregator.RunAsync((IQueryable<TEntity>)await computed.RowsAsync(services, filter, cancellationToken), plan, cancellationToken);
        }

        var scope = ODataNavigationScope.For(services, typeof(TEntity));
        return await ReportAggregator.RunAsync(await ODataEntityAccess.FilteredAsync<TEntity>(services, filter, cancellationToken, scope), plan, cancellationToken, scope);
    }

    private static Task CheckFilterAsync<TEntity>(IServiceProvider services, string? filter, CancellationToken cancellationToken)
        where TEntity : class =>
        ODataEntityAccess.FilteredAsync<TEntity>(services, filter, cancellationToken);
}
