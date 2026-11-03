using FluentValidation;

namespace Coworkee.Workflows;

internal sealed class DecideApprovalValidator : AbstractValidator<DecideApprovalCommand>
{
    public DecideApprovalValidator() => RuleFor(c => c.Comment).MaximumLength(WorkflowLimits.Comment);
}
