using FluentValidation;

namespace Coworkee.Identity.Groups;

internal sealed class MoveGroupValidator : AbstractValidator<MoveGroup>
{
    public MoveGroupValidator() => RuleFor(c => c.Id).NotEmpty();
}
