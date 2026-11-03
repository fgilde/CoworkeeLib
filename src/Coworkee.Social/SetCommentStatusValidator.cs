using FluentValidation;

namespace Coworkee.Social;

internal sealed class SetCommentStatusValidator : AbstractValidator<SetCommentStatusCommand>
{
    public SetCommentStatusValidator() => RuleFor(c => c.Status).IsInEnum();
}
