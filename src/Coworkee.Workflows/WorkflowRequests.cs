using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Workflows;
using Coworkee.Core.Results;
using FluentValidation;

namespace Coworkee.Workflows;

[RequiresPermission(WorkflowPermissions.View)]
[Description("Reads the workflow state of an entity, the transitions the user may run now, the open approval and the history.")]
public sealed record GetWorkflowQuery(string EntityType, Guid EntityId) : IQuery<Result<WorkflowStatusDto>>;

[RequiresPermission(WorkflowPermissions.View)]
[Description("Reads the workflow states of several entities of one type, with the transitions the user may run and the open approval, without the history.")]
public sealed record GetWorkflowStatesQuery(string EntityType, IReadOnlyList<Guid> Ids) : IQuery<Result<IReadOnlyList<WorkflowStatusDto>>>;

[RequiresPermission(WorkflowPermissions.Transition)]
[Description("Runs a transition of an entity's workflow, e.g. Submit; the transition's guards decide whether the user may.")]
public sealed record RunTransitionCommand(string EntityType, Guid EntityId, string Transition, string? Comment = null) : ICommand<Result<WorkflowStatusDto>>;

[RequiresPermission(WorkflowPermissions.View)]
[Description("Approves or rejects an open approval the user is an approver of.")]
public sealed record DecideApprovalCommand(Guid ApprovalId, bool Approve, string? Comment = null) : ICommand<Result<WorkflowStatusDto>>;

[RequiresPermission(WorkflowPermissions.View)]
[Description("Lists the open approvals waiting for the user's decision, oldest first.")]
public sealed record GetMyApprovalsQuery : IQuery<Result<IReadOnlyList<ApprovalDto>>>;

internal static class WorkflowLimits
{
    public const int EntityType = 100;
    public const int Name = 100;
    public const int Comment = 2000;
    public const int Title = 300;
    public const int Link = 2000;
}

internal sealed class GetWorkflowStatesValidator : AbstractValidator<GetWorkflowStatesQuery>
{
    public const int MaxIds = 200;

    public GetWorkflowStatesValidator() => RuleFor(q => q.Ids).NotNull().Must(ids => ids.Count <= MaxIds).WithMessage($"At most {MaxIds} ids.");
}

internal sealed class RunTransitionValidator : AbstractValidator<RunTransitionCommand>
{
    public RunTransitionValidator()
    {
        RuleFor(c => c.Transition).NotEmpty().MaximumLength(WorkflowLimits.Name);
        RuleFor(c => c.Comment).MaximumLength(WorkflowLimits.Comment);
    }
}

internal sealed class DecideApprovalValidator : AbstractValidator<DecideApprovalCommand>
{
    public DecideApprovalValidator() => RuleFor(c => c.Comment).MaximumLength(WorkflowLimits.Comment);
}
