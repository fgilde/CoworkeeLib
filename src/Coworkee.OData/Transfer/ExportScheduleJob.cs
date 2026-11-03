using Coworkee.Application;
using Coworkee.BackgroundJobs;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.OData.Transfer;

/// <summary>Runs the due export schedules, organisation by organisation, each as its owner.</summary>
public sealed class ExportScheduleJob(CoworkeeDbContext db, TenantJobRunner tenants, IDistributedLock locks, TimeProvider clock) : IRecurringJob
{
    public const string Id = "coworkee-export-schedules";
    public const string Cron = "*/5 * * * *";

    // ponytail: at most this many exports per run; the rest follow five minutes later
    private const int Batch = 100;

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await using var held = await locks.AcquireAsync(Id, TimeSpan.Zero, cancellationToken);
        if (held is null)
        {
            return;
        }

        var now = clock.GetUtcNow();
        var due = await db.Set<ExportSchedule>().IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.NextRunAt <= now)
            .OrderBy(s => s.NextRunAt)
            .Take(Batch)
            .Select(s => new { s.Id, s.TenantId, s.UserId })
            .ToListAsync(cancellationToken);
        var byTenant = due.ToLookup(s => s.TenantId);
        await tenants.ForTenantsAsync(byTenant.Select(g => g.Key), async (services, tenantId, ct) =>
        {
            foreach (var schedule in byTenant[tenantId])
            {
                // the owner's permissions, row filters and groups decide what the file holds
                using var actor = CurrentUserScope.Begin(new ImpersonatedUser(schedule.UserId, tenantId));
                await using var scope = services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
                var scopedDb = scope.ServiceProvider.GetRequiredService<CoworkeeDbContext>();
                if (await scopedDb.Set<ExportSchedule>().SingleOrDefaultAsync(s => s.Id == schedule.Id, ct) is { } row)
                {
                    await scope.ServiceProvider.GetRequiredService<ExportScheduleRunner>().RunAsync(row, now, advance: true, ct);
                    await scopedDb.SaveChangesAsync(ct);
                }
            }
        }, cancellationToken);
    }
}
