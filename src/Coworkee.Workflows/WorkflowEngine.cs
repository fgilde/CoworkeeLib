using Coworkee.Application.Authorization;
using Coworkee.Contracts.Workflows;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Workflows;

/// <summary>An entity with its workflow and state, loaded for one request.</summary>
public sealed record WorkflowSubject(EntityWorkflow Workflow, object Entity, WorkflowItem Item);

/// <summary>Runs transitions, decisions and deadlines; the caller saves (the dispatcher after a command, the job itself).</summary>
public sealed class WorkflowEngine(
    CoworkeeDbContext db, WorkflowOptions options, IPermissionChecker permissions, ICurrentUser currentUser, ApproverDirectory directory, TimeProvider clock, IServiceProvider services)
{
    public static readonly Error NotFound = Error.NotFound("workflows.not_found", "There is no such entity with a workflow.");

    public static readonly Error Forbidden = Error.Forbidden("workflows.forbidden", "You may not run this transition.");

    public async Task<Result<WorkflowSubject>> LoadAsync(string entityType, Guid entityId, CancellationToken cancellationToken)
    {
        if (options.For(entityType) is not { } workflow || await workflow.FindAsync(db, entityId, cancellationToken) is not { } entity)
        {
            return NotFound;
        }

        var item = await db.Set<WorkflowItem>().SingleOrDefaultAsync(i => i.EntityType == entityType && i.EntityId == entityId, cancellationToken);
        if (item is null)
        {
            item = new WorkflowItem { TenantId = currentUser.TenantId!.Value, EntityType = entityType, EntityId = entityId, State = workflow.InitialState!, ChangedAt = clock.GetUtcNow() };
            db.Add(item);
            workflow.Mirror?.Invoke(entity, item.State);
        }

        return new WorkflowSubject(workflow, entity, item);
    }

    /// <summary>The transitions out of the current state that the current user passes every guard of.</summary>
    public async Task<IReadOnlyList<WorkflowTransition>> AllowedAsync(WorkflowSubject subject, CancellationToken cancellationToken)
    {
        var allowed = new List<WorkflowTransition>();
        if (!await permissions.IsGrantedAsync(WorkflowPermissions.Transition, cancellationToken))
        {
            return allowed;
        }

        foreach (var transition in subject.Workflow.Transitions.Where(t => t.From.Contains(subject.Item.State)))
        {
            if (await PassesAsync(subject, transition, null, cancellationToken))
            {
                allowed.Add(transition);
            }
        }

        return allowed;
    }

    public async Task<Result> RunAsync(WorkflowSubject subject, string transitionName, string? comment, CancellationToken cancellationToken)
    {
        var transition = subject.Workflow.Transitions.FirstOrDefault(t => t.Name == transitionName && t.From.Contains(subject.Item.State));
        if (transition is null)
        {
            return Error.Conflict("workflows.not_possible", $"\"{transitionName}\" is not possible in the state \"{subject.Item.State}\".");
        }

        if (transition.CommentRequired && string.IsNullOrWhiteSpace(comment))
        {
            return Error.Validation("comment", "This transition needs a comment.");
        }

        if (!await PassesAsync(subject, transition, comment, cancellationToken))
        {
            return Forbidden;
        }

        await MoveAsync(subject, new WorkflowMove(subject.Item.State, transition.To, transition.Name, comment, currentUser.UserId), WorkflowStepKind.Transition, transition.Actions, cancellationToken);
        return Result.Success();
    }

    /// <summary>A decision of the current user on the open approval; the last needed approval or any rejection moves the workflow on.</summary>
    public async Task<Result> DecideAsync(WorkflowSubject subject, WorkflowApproval approval, bool approve, string? comment, CancellationToken cancellationToken)
    {
        var me = currentUser.UserId;
        var approver = approval.Approvers.FirstOrDefault(a => a.UserId == me && a.Approved is null);
        if (approval.Status != ApprovalStatus.Pending || approver is null)
        {
            return Error.Forbidden("workflows.not_an_approver", "You have no open decision on this approval.");
        }

        approver.Approved = approve;
        approver.Comment = comment;
        approver.DecidedAt = clock.GetUtcNow();
        AddStep(subject.Item, approve ? WorkflowStepKind.Approved : WorkflowStepKind.Rejected, subject.Item.State, null, null, comment, me);

        var regular = approval.Approvers.Where(a => !a.Escalation).ToList();
        var done = !approve || approver.Escalation || !approval.RequiresAll || regular.All(a => a.Approved == true);
        if (done && subject.Workflow.FindState(approval.State)?.Step is { } step)
        {
            await FinishAsync(subject, approval, approve ? ApprovalStatus.Approved : ApprovalStatus.Rejected, approve ? step.ApprovedState : step.RejectedState, comment, me, cancellationToken);
        }

        subject.Item.ChangedAt = clock.GetUtcNow();
        return Result.Success();
    }

    /// <summary>Reminder and deadline of one open approval, as the system; called by the deadline job.</summary>
    public async Task TickAsync(WorkflowSubject subject, WorkflowApproval approval, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var step = subject.Workflow.FindState(approval.State)?.Step;
        var context = subject.Workflow.Context(subject.Entity, subject.Item.EntityId, new(subject.Item.State, subject.Item.State, null, null, null), services, cancellationToken);
        var open = approval.Approvers.Where(a => a.Approved is null).Select(a => a.UserId).ToList();
        if (approval.RemindAt <= now && approval.RemindedAt is null)
        {
            approval.RemindedAt = now;
            AddStep(subject.Item, WorkflowStepKind.Reminded, subject.Item.State, null, null, null, null);
            await context.NotifyAsync(open, "Reminder: approval needed", "{0} still waits for your decision.");
        }

        if (approval.DueAt <= now && approval.EscalatedAt is null && step is not null)
        {
            approval.EscalatedAt = now;
            if (step.TimeoutState is { } timeout)
            {
                await FinishAsync(subject, approval, ApprovalStatus.Expired, timeout, null, null, cancellationToken);
            }
            else
            {
                var added = (await directory.UsersInRoleAsync(step.EscalationRoles, cancellationToken)).Where(u => approval.Approvers.All(a => a.UserId != u)).ToList();
                // added explicitly: with its id already set EF would take a new approver found through the collection for an existing one
                db.AddRange(added.Select(u => new WorkflowApprover { ApprovalId = approval.Id, UserId = u, Escalation = true }));
                AddStep(subject.Item, WorkflowStepKind.Escalated, subject.Item.State, null, null, null, null);
                await context.NotifyAsync(open.Concat(added), "Approval overdue", "The approval of {0} is overdue.");
            }
        }

        subject.Item.ChangedAt = now;
    }

    private async Task FinishAsync(WorkflowSubject subject, WorkflowApproval approval, ApprovalStatus status, string to, string? comment, Guid? userId, CancellationToken cancellationToken)
    {
        approval.Status = status;
        approval.DecidedAt = clock.GetUtcNow();
        subject.Item.Publish(new ApprovalDecided(approval.Id, approval.EntityType, approval.EntityId, status, userId, comment));
        if (status == ApprovalStatus.Expired)
        {
            AddStep(subject.Item, WorkflowStepKind.Expired, subject.Item.State, null, null, null, null);
        }

        await MoveAsync(subject, new WorkflowMove(subject.Item.State, to, null, comment, userId), null, [], cancellationToken);
    }

    private async Task<bool> PassesAsync(WorkflowSubject subject, WorkflowTransition transition, string? comment, CancellationToken cancellationToken)
    {
        if (transition.Permission is { } permission && !await permissions.IsGrantedAsync(permission, cancellationToken))
        {
            return false;
        }

        if (transition.Roles.Count > 0 && !await directory.CurrentUserHasRoleAsync(transition.Roles, cancellationToken))
        {
            return false;
        }

        var context = subject.Workflow.Context(subject.Entity, subject.Item.EntityId,
            new(subject.Item.State, transition.To, transition.Name, comment, currentUser.UserId), services, cancellationToken);
        foreach (var guard in transition.Guards)
        {
            if (!await guard(context))
            {
                return false;
            }
        }

        return true;
    }

    // kind null: the move of a decision or deadline, whose own step is already in the history
    private async Task MoveAsync(
        WorkflowSubject subject, WorkflowMove move, WorkflowStepKind? kind, IEnumerable<Func<WorkflowContext, Task>> transitionActions, CancellationToken cancellationToken)
    {
        var (workflow, entity, item) = subject;
        var context = workflow.Context(entity, item.EntityId, move, services, cancellationToken);
        await CancelOpenApprovalAsync(item, cancellationToken);
        foreach (var action in workflow.FindState(move.From)?.OnExitActions ?? [])
        {
            await action(context);
        }

        item.State = move.To;
        item.ChangedAt = clock.GetUtcNow();
        workflow.Mirror?.Invoke(entity, move.To);
        AddStep(item, kind ?? WorkflowStepKind.Transition, move.From, move.To, move.Transition, kind is null ? null : move.Comment, move.UserId);
        item.Publish(new WorkflowTransitioned(item.EntityType, item.EntityId, move.From, move.To, move.Transition, move.UserId, move.Comment));
        foreach (var action in transitionActions.Concat(workflow.FindState(move.To)?.OnEnterActions ?? []))
        {
            await action(context);
        }

        if (workflow.FindState(move.To)?.Step is { } step)
        {
            await StartApprovalAsync(subject, context, step, move.UserId, cancellationToken);
        }
    }

    private async Task StartApprovalAsync(WorkflowSubject subject, WorkflowContext context, ApprovalStep step, Guid? requester, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var users = (await directory.UsersInRoleAsync(step.Roles, cancellationToken)).Concat(step.Users).Distinct()
            .Where(u => !step.FourEyes || u != requester).ToList();
        var approval = new WorkflowApproval
        {
            TenantId = subject.Item.TenantId,
            EntityType = subject.Item.EntityType,
            EntityId = subject.Item.EntityId,
            State = subject.Item.State,
            Title = subject.Workflow.Title(subject.Entity, subject.Item.EntityId),
            Link = subject.Workflow.LinkOf?.Invoke(subject.Item.EntityId),
            RequiresAll = step.RequiresAll,
            Status = ApprovalStatus.Pending,
            CreatedAt = now,
            DueAt = now + step.Deadline,
            RemindAt = now + step.Reminder,
        };
        approval.Approvers.AddRange(users.Select(u => new WorkflowApprover { ApprovalId = approval.Id, UserId = u }));
        db.Add(approval);
        subject.Item.Publish(new ApprovalStarted(approval.Id, approval.EntityType, approval.EntityId, approval.State, users));
        await context.NotifyAsync(users, "Approval requested", "{0} waits for your approval.");
    }

    // tracked ones too: an approval decided a moment ago in this request is already finished in memory, not yet in the database
    private async Task CancelOpenApprovalAsync(WorkflowItem item, CancellationToken cancellationToken)
    {
        await db.Set<WorkflowApproval>().Where(a => a.EntityType == item.EntityType && a.EntityId == item.EntityId && a.Status == ApprovalStatus.Pending)
            .LoadAsync(cancellationToken);
        foreach (var approval in db.ChangeTracker.Entries<WorkflowApproval>().Select(e => e.Entity)
                     .Where(a => a.EntityType == item.EntityType && a.EntityId == item.EntityId && a.Status == ApprovalStatus.Pending).ToList())
        {
            approval.Status = ApprovalStatus.Canceled;
            approval.DecidedAt = clock.GetUtcNow();
        }
    }

    // ponytail: steps of one request share the clock's time; 0.1 ms each (the precision SQLite keeps) holds their order. A sequence column if requests ever race that closely
    private int _sequence;

    private void AddStep(WorkflowItem item, WorkflowStepKind kind, string? from, string? to, string? transition, string? comment, Guid? userId) =>
        db.Add(new WorkflowStep
        {
            TenantId = item.TenantId,
            EntityType = item.EntityType,
            EntityId = item.EntityId,
            Kind = kind,
            From = from,
            To = to,
            Transition = transition,
            Comment = comment,
            UserId = userId,
            At = clock.GetUtcNow().AddTicks(1000 * _sequence++),
        });
}
