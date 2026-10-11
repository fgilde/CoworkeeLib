using System.Diagnostics;
using System.Reflection;
using Coworkee.Application.Authorization;
using Coworkee.Contracts.Reporting;
using Coworkee.Core.Results;
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
            .Where(s => s.Registration.Permission is null || granted.Contains(s.Registration.Permission))
            .Select(s => s with { Fields = [.. s.Fields.Where(f => f.Permission is null || granted.Contains(f.Permission))] })
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
            var rows = await (Task<Result<IReadOnlyList<ReportQueryRow>>>)RunMethod.MakeGenericMethod(source!.Registration.EntityType)
                .Invoke(null, [services, request.Filter, plan.Value, cancellationToken])!;
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
            await (Task)CheckMethod.MakeGenericMethod(source!.Registration.EntityType).Invoke(null, [services, request.Filter, cancellationToken])!;
            return null;
        }
        catch (ODataException exception)
        {
            return Error.Validation(nameof(ReportQueryRequest.Filter), exception.Message);
        }
    }

    private async Task<(ReportSource? Source, Result<ReportPlan> Plan)> PlanAsync(ReportQueryRequest request, CancellationToken cancellationToken)
    {
        if (schema.Find(request.EntitySet) is not { } source)
        {
            return (null, Error.NotFound("reports.unknown_set", $"There is no data set '{request.EntitySet}'."));
        }

        var granted = await GrantedAsync(cancellationToken);
        if (source.Registration.Permission is { } permission && !granted.Contains(permission))
        {
            return (null, Error.Forbidden("reports.forbidden_set", $"You may not read {source.Registration.EntitySet}."));
        }

        return (source, ReportPlan.Create(source, request, granted));
    }

    private async Task<IReadOnlySet<string>> GrantedAsync(CancellationToken cancellationToken) =>
        _granted ??= (await permissions.GetGrantedAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);

    private static async Task<Result<IReadOnlyList<ReportQueryRow>>> RunTypedAsync<TEntity>(IServiceProvider services, string? filter, ReportPlan plan, CancellationToken cancellationToken)
        where TEntity : class =>
        await ReportAggregator.RunAsync(await ODataEntityAccess.FilteredAsync<TEntity>(services, filter, cancellationToken), plan, cancellationToken);

    private static Task CheckFilterAsync<TEntity>(IServiceProvider services, string? filter, CancellationToken cancellationToken)
        where TEntity : class =>
        ODataEntityAccess.FilteredAsync<TEntity>(services, filter, cancellationToken);
}
