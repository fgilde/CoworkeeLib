using FluentValidation;

namespace Coworkee.Workflows;

internal sealed class GetWorkflowStatesValidator : AbstractValidator<GetWorkflowStatesQuery>
{
    public const int MaxIds = 200;

    public GetWorkflowStatesValidator() => RuleFor(q => q.Ids).NotNull().Must(ids => ids.Count <= MaxIds).WithMessage($"At most {MaxIds} ids.");
}
