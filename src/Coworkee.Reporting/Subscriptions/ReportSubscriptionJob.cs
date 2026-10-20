using Coworkee.Application;
using Coworkee.BackgroundJobs;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Reporting.Subscriptions;

/// <summary>Mails the due subscriptions, organisation by organisation, each in a scope of its subscriber.</summary>
public sealed class ReportSubscriptionJob(CoworkeeDbContext db, TenantJobRunner tenants, IDistributedLock locks, TimeProvider clock) : IRecurringJob
{
    public const string Id = "coworkee-report-subscriptions";
    public const string DefaultCron = "*/5 * * * *";

    // ponytail: at most this many mails per run; the rest follow five minutes later
    private const int Batch = 200;

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await using var held = await locks.AcquireAsync(Id, TimeSpan.Zero, cancellationToken);
        if (held is null)
        {
            return;
        }

        var now = clock.GetUtcNow();
        var due = await db.Set<ReportSubscription>().IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.NextRunAt <= now)
            .OrderBy(s => s.NextRunAt)
            .Take(Batch)
            .Select(s => new { s.Id, s.TenantId, s.UserId })
            .ToListAsync(cancellationToken);
        var byTenant = due.ToLookup(s => s.TenantId);
        await tenants.ForTenantsAsync(byTenant.Select(g => g.Key), async (services, tenantId, ct) =>
        {
            foreach (var subscription in byTenant[tenantId])
            {
                await RunAsUserAsync(services, subscription.Id, subscription.UserId, tenantId, now, advance: true, ct);
            }
        }, cancellationToken);
    }

    /// <summary>The subscriber's permissions, row filters and organisation decide what the mail shows, not the job's.</summary>
    internal static async Task RunAsUserAsync(IServiceProvider services, Guid subscriptionId, Guid userId, Guid tenantId, DateTimeOffset now, bool advance, CancellationToken cancellationToken)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(userId, tenantId));
        await using var scope = services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ReportSubscriptionMail>().RunAsync(subscriptionId, now, advance, cancellationToken);
    }
}
