using System.Diagnostics;
using System.Reflection;
using Coworkee.Application.Authorization;
using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Reporting;
using Coworkee.Core.Results;
using Coworkee.Infrastructure.Persistence;
using Coworkee.OData;
using Microsoft.Extensions.Logging;
using Microsoft.OData;

namespace Coworkee.Reporting.Querying;

/// <summary>Sources the user may read and aggregates over them, with the entity set's permission, tenant and row filters.</summary>
internal sealed class ReportQueries(IServiceProvider services, ReportSchema schema, IPermissionChecker permissions, ILogger<ReportQueries> log)
{
    private static readonly MethodInfo RunMethod = typeof(ReportQueries).GetMethod(nameof(RunTypedAsync), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo TableMethod = typeof(ReportQueries).GetMethod(nameof(TableTypedAsync), BindingFlags.NonPublic | BindingFlags.Static)!;
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

    /// <summary>The ISO code the source states for the current user (a computed source with a currency resolver), null otherwise.</summary>
    /// <remarks>The resolver is app code: when it throws the source states no currency (a warning is logged) instead of breaking the source list or a mail.</remarks>
    public async Task<string?> CurrencyAsync(ReportSource source, CancellationToken cancellationToken)
    {
        if (source.Computed is not { } computed)
        {
            return null;
        }

        try
        {
            return await computed.CurrencyAsync(services, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            log.LogWarning(exception, "The currency resolver of the report source {Source} failed; the source states no currency.", source.EntitySet);
            return null;
        }
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
                computed.Parse(request.Filter, services);
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

        var (provider, binaryDate) = Storage(source.Value);
        return (source.Value, ReportPlan.Create(source.Value, request, await GrantedAsync(cancellationToken), provider, binaryDate));
    }

    // an app source aggregates in memory unless it hands out a query of the database
    private (string Provider, Func<ReportField, bool>? BinaryDate) Storage(ReportSource source)
    {
        if (source.Computed is not null)
        {
            return (DatabaseProviders.Postgres, null);
        }

        var db = services.DbContextFor(source.RowType);
        return (CoworkeeDatabase.Provider(db.Database), field => StoredAsNumber(db.Model, source.RowType, field.Chain));
    }

    /// <summary>A grouped table: every source readable (Forbidden otherwise) and grouped by all group fields, the joins merged onto the main groups.</summary>
    public async Task<Result<ReportTableResult>> TableAsync(ReportTableRequest request, CancellationToken cancellationToken)
    {
        var fields = request.GroupFields ?? [];
        var joins = request.Joins ?? [];
        var orderBy = request.OrderBy ?? [];
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (fields.Count is < 1 or > ReportTables.MaxGroupFields)
        {
            errors[nameof(request.GroupFields)] = [$"Group by 1 to {ReportTables.MaxGroupFields} fields."];
        }

        if (joins.Count > ReportTables.MaxJoins)
        {
            errors[nameof(request.Joins)] = [$"At most {ReportTables.MaxJoins} joins."];
        }

        if (request.Top < 1 || request.Top > ReportTables.MaxRows)
        {
            errors[nameof(request.Top)] = [$"Top is between 1 and {ReportTables.MaxRows}."];
        }

        if (request.Skip < 0)
        {
            errors[nameof(request.Skip)] = ["Skip is 0 or more."];
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var sets = new List<(ReportSource Source, string? Filter, IReadOnlyList<ReportAggregate> Aggregates, string Prefix)>();
        foreach (var (set, filter, aggregates, prefix) in joins.Select((j, i) => (j.EntitySet, j.Filter, j.Aggregates, $"{nameof(request.Joins)}[{i}].")).Prepend((request.EntitySet, request.Filter, request.Aggregates, "")))
        {
            var source = await ReadableAsync(set, cancellationToken);
            if (!source.IsSuccess)
            {
                return source.Error;
            }

            sets.Add((source.Value, filter, aggregates ?? [], prefix));
        }

        var granted = await GrantedAsync(cancellationToken);
        var plans = sets.Select(s =>
        {
            var (provider, binaryDate) = Storage(s.Source);
            var groups = fields.Select((f, i) => ReportPlan.GroupOf(s.Source, new ReportGroupBy(f), $"{s.Prefix}{nameof(request.GroupFields)}[{i}]", granted, errors, provider, binaryDate)).ToList();
            if (s.Aggregates.Count > ReportLimits.Aggregates)
            {
                errors[$"{s.Prefix}{nameof(request.Aggregates)}"] = [$"At most {ReportLimits.Aggregates} values."];
            }

            var values = s.Aggregates.Select((a, i) => ReportPlan.Value(s.Source, a, $"{s.Prefix}{nameof(request.Aggregates)}[{i}]", granted, errors)).ToList();
            return (s.Source, s.Filter, s.Prefix, Groups: (IReadOnlyList<ReportGroup>)groups!, Values: (IReadOnlyList<ReportValue>)values!);
        }).ToList();

        var columns = fields.Count + plans.Sum(p => p.Values.Count);
        foreach (var (sort, i) in orderBy.Select((sort, i) => (sort, i)).Where(s => s.sort.Column < 0 || s.sort.Column >= columns))
        {
            errors[$"{nameof(request.OrderBy)}[{i}]"] = [$"Column {sort.Column} is not between 0 and {columns - 1}."];
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var watch = Stopwatch.StartNew();
        var grouped = new List<List<(object?[] Keys, double?[] Values)>>();
        foreach (var plan in plans)
        {
            try
            {
                var rows = await (Task<List<(object?[] Keys, double?[] Values)>?>)TableMethod.MakeGenericMethod(plan.Source.RowType)
                    .Invoke(null, [services, plan.Source, plan.Filter, plan.Groups, plan.Values, plan.Prefix.Length > 0, cancellationToken])!;
                if (rows is null)
                {
                    return Error.Validation($"{plan.Prefix}{nameof(request.GroupFields)}", $"More than {ReportTables.MaxRows} groups in {plan.Source.EntitySet}; filter the rows or group by fewer fields.");
                }

                grouped.Add(rows);
            }
            catch (ODataException exception)
            {
                return Error.Validation($"{plan.Prefix}{nameof(request.Filter)}", exception.Message);
            }
        }

        var (page, more) = ReportTables.Merge(plans[0].Groups, grouped[0], [.. plans.Skip(1).Select((p, i) => (p.Groups, grouped[i + 1], p.Values.Count))], orderBy, request.Skip, request.Top);
        return new ReportTableResult(page, more, watch.ElapsedMilliseconds);
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

    // the same rows as RunTypedAsync: a computed source's own, an entity set's with its row filters, data filters and navigation scope
    private static async Task<List<(object?[] Keys, double?[] Values)>?> TableTypedAsync<TEntity>(
        IServiceProvider services, ReportSource source, string? filter, IReadOnlyList<ReportGroup> groups, IReadOnlyList<ReportValue> values, bool join, CancellationToken cancellationToken)
        where TEntity : class
    {
        if (source.Computed is { } computed)
        {
            return await ReportTables.GroupAsync((IQueryable<TEntity>)await computed.RowsAsync(services, filter, cancellationToken), groups, values, join, cancellationToken);
        }

        var scope = ODataNavigationScope.For(services, typeof(TEntity));
        return await ReportTables.GroupAsync(await ODataEntityAccess.FilteredAsync<TEntity>(services, filter, cancellationToken, scope), groups, values, join, cancellationToken, scope);
    }

    private static Task CheckFilterAsync<TEntity>(IServiceProvider services, string? filter, CancellationToken cancellationToken)
        where TEntity : class =>
        ODataEntityAccess.FilteredAsync<TEntity>(services, filter, cancellationToken);
}
