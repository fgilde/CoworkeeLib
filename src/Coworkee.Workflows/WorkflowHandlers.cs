using Coworkee.Application.Messaging;
using Coworkee.Contracts.Workflows;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Workflows;

internal sealed class WorkflowHandlers(CoworkeeDbContext db, WorkflowEngine engine, ICurrentUser currentUser)
    : IHandler<GetWorkflowQuery, Result<WorkflowStatusDto>>,
      IHandler<GetWorkflowStatesQuery, Result<IReadOnlyList<WorkflowStatusDto>>>,
      IHandler<RunTransitionCommand, Result<WorkflowStatusDto>>,
      IHandler<DecideApprovalCommand, Result<WorkflowStatusDto>>,
      IHandler<GetMyApprovalsQuery, Result<IReadOnlyList<ApprovalDto>>>
{
    private static readonly Error UnknownApproval = Error.NotFound("workflows.approval_not_found", "The approval does not exist.");

    public async Task<Result<WorkflowStatusDto>> HandleAsync(GetWorkflowQuery query, CancellationToken cancellationToken)
    {
        var subject = await engine.LoadAsync(query.EntityType, query.EntityId, cancellationToken, WorkflowAccess.SeeOrDecide);
        return subject.IsSuccess ? await StatusAsync(subject.Value, cancellationToken) : subject.Error;
    }

    // one request for a table page; rows the user cannot see (or that do not exist) are left out
    public async Task<Result<IReadOnlyList<WorkflowStatusDto>>> HandleAsync(GetWorkflowStatesQuery query, CancellationToken cancellationToken)
    {
        var states = new List<WorkflowStatusDto>();
        foreach (var id in query.Ids.Distinct())
        {
            if (await engine.LoadAsync(query.EntityType, id, cancellationToken, WorkflowAccess.SeeOrDecide) is { IsSuccess: true } subject)
            {
                states.Add(await StatusAsync(subject.Value, cancellationToken, history: false));
            }
        }

        return states;
    }

    public async Task<Result<WorkflowStatusDto>> HandleAsync(RunTransitionCommand command, CancellationToken cancellationToken)
    {
        var subject = await engine.LoadAsync(command.EntityType, command.EntityId, cancellationToken);
        if (!subject.IsSuccess)
        {
            return subject.Error;
        }

        var run = await engine.RunAsync(subject.Value, command.Transition, command.Comment?.Trim(), cancellationToken);
        return run.IsSuccess ? await SaveAsync(subject.Value, cancellationToken) : run.Error;
    }

    public async Task<Result<WorkflowStatusDto>> HandleAsync(DecideApprovalCommand command, CancellationToken cancellationToken)
    {
        var approval = await db.Set<WorkflowApproval>().Include(a => a.Approvers).SingleOrDefaultAsync(a => a.Id == command.ApprovalId, cancellationToken);
        if (approval is null)
        {
            return UnknownApproval;
        }

        var subject = await engine.LoadAsync(approval.EntityType, approval.EntityId, cancellationToken, WorkflowAccess.SeeOrDecide);
        if (!subject.IsSuccess)
        {
            return subject.Error;
        }

        var decided = await engine.DecideAsync(subject.Value, approval, command.Approve, command.Comment?.Trim(), cancellationToken);
        return decided.IsSuccess ? await SaveAsync(subject.Value, cancellationToken) : decided.Error;
    }

    public async Task<Result<IReadOnlyList<ApprovalDto>>> HandleAsync(GetMyApprovalsQuery query, CancellationToken cancellationToken)
    {
        var me = currentUser.UserId;
        var approvals = await db.Set<WorkflowApproval>().AsNoTracking().Include(a => a.Approvers)
            .Where(a => a.Status == ApprovalStatus.Pending && a.Approvers.Any(x => x.UserId == me && x.Approved == null))
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(cancellationToken);
        return approvals.Select(ToDto).ToList();
    }

    private async Task<WorkflowStatusDto> SaveAsync(WorkflowSubject subject, CancellationToken cancellationToken)
    {
        await db.SaveChangesAsync(cancellationToken);
        return await StatusAsync(subject, cancellationToken);
    }

    private async Task<WorkflowStatusDto> StatusAsync(WorkflowSubject subject, CancellationToken cancellationToken, bool history = true)
    {
        var item = subject.Item;
        var allowed = await engine.AllowedAsync(subject, cancellationToken);
        var approval = await db.Set<WorkflowApproval>().AsNoTracking().Include(a => a.Approvers)
            .Where(a => a.EntityType == item.EntityType && a.EntityId == item.EntityId && a.Status == ApprovalStatus.Pending)
            .FirstOrDefaultAsync(cancellationToken);
        var steps = !history ? [] : await db.Set<WorkflowStep>().AsNoTracking()
            .Where(s => s.EntityType == item.EntityType && s.EntityId == item.EntityId)
            .OrderBy(s => s.At)
            .Select(s => new WorkflowStepDto(s.Kind, s.From, s.To, s.Transition, s.UserId, s.Comment, s.At))
            .ToListAsync(cancellationToken);
        return new WorkflowStatusDto(item.EntityType, item.EntityId, item.State, allowed.Select(t => new WorkflowTransitionDto(t.Name, t.To, t.CommentRequired)).ToList(),
            approval is null ? null : ToDto(approval), steps);
    }

    private ApprovalDto ToDto(WorkflowApproval a) => new(
        a.Id, a.EntityType, a.EntityId, a.State, a.Title, a.RequiresAll, a.Status, a.CreatedAt, a.DueAt,
        a.Approvers.Select(x => new ApproverDto(x.UserId, x.Approved, x.Comment, x.DecidedAt, x.Escalation)).ToList(),
        a.Status == ApprovalStatus.Pending && a.Approvers.Any(x => x.UserId == currentUser.UserId && x.Approved is null),
        a.Link);
}
