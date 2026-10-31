using Coworkee.Application.Privacy;
using Coworkee.Contracts.Workflows;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Workflows;

/// <summary>
/// The user's steps in the workflow histories, his decisions and the approvals he asked for. Erasing keeps the histories and decisions, without his id
/// (<see cref="PersonalDataSubject.ErasedUserId"/>) and without the comments he wrote. He leaves the pending approvals; one that then waits for nobody is
/// overdue at once, so the deadline job moves it to its timeout state or adds the escalation roles; one already escalated is cancelled.
/// </summary>
internal sealed class WorkflowPersonalData(CoworkeeDbContext db, TimeProvider clock) : IPersonalDataContributor
{
    public string Section => "workflows";

    public async Task<object?> ExportAsync(PersonalDataSubject subject, CancellationToken cancellationToken)
    {
        var userId = subject.UserId;
        return new
        {
            Steps = await db.Set<WorkflowStep>().IgnoreQueryFilters().AsNoTracking().Where(s => s.UserId == userId).OrderBy(s => s.At)
                .Select(s => new { s.EntityType, s.EntityId, s.Kind, s.From, s.To, s.Transition, s.Comment, s.At }).ToListAsync(cancellationToken),
            Decisions = await (from approver in db.Set<WorkflowApprover>().AsNoTracking()
                               join approval in db.Set<WorkflowApproval>().IgnoreQueryFilters() on approver.ApprovalId equals approval.Id
                               where approver.UserId == userId
                               orderby approval.CreatedAt
                               select new { approval.Title, approval.EntityType, approval.EntityId, approval.State, approval.Status, approver.Approved, approver.Comment, approver.DecidedAt })
                .ToListAsync(cancellationToken),
            Requested = await db.Set<WorkflowApproval>().IgnoreQueryFilters().AsNoTracking().Where(a => a.RequestedBy == userId).OrderBy(a => a.CreatedAt)
                .Select(a => new { a.Title, a.EntityType, a.EntityId, a.State, a.Status, a.CreatedAt, a.DecidedAt }).ToListAsync(cancellationToken),
        };
    }

    public async Task EraseAsync(PersonalDataSubject subject, CancellationToken cancellationToken)
    {
        var userId = subject.UserId;
        var now = clock.GetUtcNow();
        // first: the row version of an approval changes with a direct update, the tracked ones below are saved later
        var erased = PersonalDataSubject.ErasedUserId;
        await db.Set<WorkflowApprover>().Where(a => a.UserId == userId && a.Approved != null)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.UserId, erased).SetProperty(a => a.Comment, (string?)null), cancellationToken);
        await db.Set<WorkflowStep>().IgnoreQueryFilters().Where(s => s.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.UserId, (Guid?)erased).SetProperty(x => x.Comment, (string?)null), cancellationToken);
        await db.Set<WorkflowApproval>().IgnoreQueryFilters().Where(a => a.RequestedBy == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.RequestedBy, (Guid?)erased), cancellationToken);

        var waiting = await db.Set<WorkflowApproval>().IgnoreQueryFilters().Include(a => a.Approvers)
            .Where(a => a.Status == ApprovalStatus.Pending && a.Approvers.Any(x => x.UserId == userId && x.Approved == null))
            .ToListAsync(cancellationToken);
        foreach (var approval in waiting)
        {
            foreach (var open in approval.Approvers.Where(x => x.UserId == userId && x.Approved == null).ToList())
            {
                approval.Approvers.Remove(open);
                db.Remove(open);
            }

            if (approval.Approvers.Any(x => x.Approved == null))
            {
                continue;
            }

            if (approval.EscalatedAt is null)
            {
                approval.DueAt = now;
            }
            else
            {
                (approval.Status, approval.DecidedAt) = (ApprovalStatus.Canceled, now);
            }
        }
    }
}
