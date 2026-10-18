using System.Globalization;
using Coworkee.Application.Messaging;
using Coworkee.Application.Retention;
using Coworkee.BackgroundJobs;
using Coworkee.Contracts.Retention;
using Coworkee.Contracts.Settings;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Retention;

internal sealed class RetentionHandlers(
    IEnumerable<IRetentionTarget> targets, CoworkeeDbContext db, ISettingProvider settings, SettingWriter writer, RetentionRunner runner, IBackgroundJobs jobs,
    ICurrentUser currentUser, IServiceProvider services, TimeProvider clock)
    : IHandler<GetRetentionTargetsQuery, Result<IReadOnlyList<RetentionTargetDto>>>,
      IHandler<CountRetentionQuery, Result<long>>,
      IHandler<SetRetentionDaysCommand, Result>,
      IHandler<RunRetentionCommand, Result>
{
    private static readonly Error SystemOnly =
        Error.Forbidden("retention.system_only", "Retention applies to every organisation; it is managed from the system organisation.");

    private static readonly Error Running = Error.Conflict("retention.running", "This data is being deleted already.");

    public async Task<Result<IReadOnlyList<RetentionTargetDto>>> HandleAsync(GetRetentionTargetsQuery query, CancellationToken cancellationToken)
    {
        if (!await IsSystemAsync(cancellationToken))
        {
            return SystemOnly;
        }

        var runs = await db.Set<RetentionRun>().AsNoTracking().ToDictionaryAsync(r => r.Key, StringComparer.Ordinal, cancellationToken);
        var now = clock.GetUtcNow();
        var list = new List<RetentionTargetDto>();
        // one after the other: the targets share the request's DbContext
        foreach (var target in targets)
        {
            var days = await DaysAsync(target.Key, cancellationToken);
            var affected = days is { } d ? await TryCountAsync(target, now.AddDays(-d), cancellationToken) : null;
            var run = runs.GetValueOrDefault(target.Key);
            list.Add(new RetentionTargetDto(target.Key, target.Title, target.Description, days, target.DefaultDays, affected,
                run?.LastRunAt, run?.LastDeleted, run?.LastError, RetentionRunner.IsRunning(run, now)));
        }

        return list;
    }

    public async Task<Result<long>> HandleAsync(CountRetentionQuery query, CancellationToken cancellationToken) =>
        !await IsSystemAsync(cancellationToken) ? SystemOnly
        : Find(query.Key) is not { } target ? NotFound(query.Key)
        : await target.CountAsync(clock.GetUtcNow().AddDays(-query.Days), cancellationToken);

    public async Task<Result> HandleAsync(SetRetentionDaysCommand command, CancellationToken cancellationToken)
    {
        if (!await IsSystemAsync(cancellationToken))
        {
            return SystemOnly;
        }

        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (key, days) in command.Days)
        {
            if (Find(key) is not { } target)
            {
                return NotFound(key);
            }

            var keep = days is > 0 ? days : null;
            // the default needs no row; "never" against a default of days is stored as 0
            values[RetentionSettingDefinitions.Name(key)] = keep == target.DefaultDays ? null : (keep ?? 0).ToString(CultureInfo.InvariantCulture);
        }

        return await writer.WriteAsync(SettingScope.Global, null, values, cancellationToken) is { } invalid ? invalid : Result.Success();
    }

    public async Task<Result> HandleAsync(RunRetentionCommand command, CancellationToken cancellationToken)
    {
        if (!await IsSystemAsync(cancellationToken))
        {
            return SystemOnly;
        }

        if (Find(command.Key) is null)
        {
            return NotFound(command.Key);
        }

        if (!await runner.TryClaimAsync(command.Key, cancellationToken))
        {
            return Running;
        }

        jobs.Enqueue<RetentionRunJob, RetentionRunArgs>(new RetentionRunArgs(command.Key, command.Days));
        return Result.Success();
    }

    private IRetentionTarget? Find(string key) => targets.FirstOrDefault(t => t.Key == key);

    private static Error NotFound(string key) => Error.NotFound("retention.not_found", $"There is no retention target '{key}'.");

    private async Task<int?> DaysAsync(string key, CancellationToken cancellationToken) =>
        RetentionSettingDefinitions.Days(await settings.GetAsync(RetentionSettingDefinitions.Name(key), cancellationToken));

    private static async Task<long?> TryCountAsync(IRetentionTarget target, DateTimeOffset before, CancellationToken cancellationToken)
    {
        try
        {
            return await target.CountAsync(before, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private async Task<bool> IsSystemAsync(CancellationToken cancellationToken) =>
        currentUser.TenantId is { } tenantId && services.GetService<ITenantDirectory>() is { } tenants && await tenants.IsSystemTenantAsync(tenantId, cancellationToken);
}
