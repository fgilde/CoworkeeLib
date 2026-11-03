using FluentValidation;

namespace Coworkee.Workflows;

internal sealed class RunTransitionValidator : AbstractValidator<RunTransitionCommand>
{
    public RunTransitionValidator()
    {
        RuleFor(c => c.Transition).NotEmpty().MaximumLength(WorkflowLimits.Name);
        RuleFor(c => c.Comment).MaximumLength(WorkflowLimits.Comment);
    }
}
