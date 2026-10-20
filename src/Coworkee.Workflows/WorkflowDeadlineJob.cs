using Coworkee.BackgroundJobs;
using Coworkee.Contracts.Workflows;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Coworkee.Workflows;

/// <summary>Sends due reminders and handles passed deadlines of open approvals, organisation by organisation, one approval per scope.</summary>
public sealed partial class WorkflowDeadlineJob(CoworkeeDbContext db, TenantJobRunner tenants, TimeProvider clock, ILogger<WorkflowDeadlineJob> logger) : IRecurringJob
{
    public const string Id = "workflow-deadlines";

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var due = await db.Set<WorkflowApproval>().IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.Status == ApprovalStatus.Pending && ((a.RemindAt <= now && a.RemindedAt == null) || (a.DueAt <= now && a.EscalatedAt == null)))
            .Select(a => new { a.Id, a.TenantId })
            .ToListAsync(cancellationToken);
        var byTenant = due.ToLookup(a => a.TenantId, a => a.Id);
        await tenants.ForTenantsAsync(byTenant.Select(g => g.Key), async (services, tenantId, ct) =>
        {
            foreach (var approvalId in byTenant[tenantId])
            {
                try
                {
                    await TickAsync(services, approvalId, ct);
                }
                catch (DbUpdateConcurrencyException exception)
                {
                    LogConflict(exception, approvalId);
                }
            }
        }, cancellationToken);
    }

    private async Task TickAsync(IServiceProvider services, Guid approvalId, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var scoped = scope.ServiceProvider.GetRequiredService<CoworkeeDbContext>();
        var approval = await scoped.Set<WorkflowApproval>().Include(a => a.Approvers).SingleAsync(a => a.Id == approvalId, cancellationToken);
        var subject = await scope.ServiceProvider.GetRequiredService<WorkflowEngine>().LoadAsync(approval.EntityType, approval.EntityId, cancellationToken, WorkflowAccess.System);
        if (subject.IsSuccess)
        {
            await scope.ServiceProvider.GetRequiredService<WorkflowEngine>().TickAsync(subject.Value, approval, cancellationToken);
        }
        else
        {
            // the entity is gone or its workflow no longer declared
            approval.Status = ApprovalStatus.Canceled;
            approval.DecidedAt = clock.GetUtcNow();
        }

        await scoped.SaveChangesAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Approval {ApprovalId} changed meanwhile; the next run looks again")]
    private partial void LogConflict(Exception exception, Guid approvalId);
}
