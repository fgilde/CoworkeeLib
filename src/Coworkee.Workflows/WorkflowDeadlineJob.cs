using Coworkee.BackgroundJobs;
using Coworkee.Contracts.Workflows;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Coworkee.Workflows;

/// <summary>Sends due reminders and handles passed deadlines of open approvals, in every organisation, one approval per scope.</summary>
public sealed partial class WorkflowDeadlineJob(CoworkeeDbContext db, IServiceScopeFactory scopes, TimeProvider clock, ILogger<WorkflowDeadlineJob> logger) : IRecurringJob
{
    public const string Id = "workflow-deadlines";

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var due = await db.Set<WorkflowApproval>().IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.Status == ApprovalStatus.Pending && ((a.RemindAt <= now && a.RemindedAt == null) || (a.DueAt <= now && a.EscalatedAt == null)))
            .Select(a => new { a.Id, a.TenantId })
            .ToListAsync(cancellationToken);
        foreach (var approval in due)
        {
            try
            {
                await TickAsync(approval.Id, approval.TenantId, cancellationToken);
            }
            catch (DbUpdateConcurrencyException exception)
            {
                LogConflict(exception, approval.Id);
            }
        }
    }

    private async Task TickAsync(Guid approvalId, Guid tenantId, CancellationToken cancellationToken)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, tenantId));
        await using var scope = scopes.CreateAsyncScope();
        var scoped = scope.ServiceProvider.GetRequiredService<CoworkeeDbContext>();
        var approval = await scoped.Set<WorkflowApproval>().Include(a => a.Approvers).SingleAsync(a => a.Id == approvalId, cancellationToken);
        var subject = await scope.ServiceProvider.GetRequiredService<WorkflowEngine>().LoadAsync(approval.EntityType, approval.EntityId, cancellationToken);
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
