using FluentValidation;

namespace Coworkee.Identity.Groups;

internal sealed class CreateGroupValidator : AbstractValidator<CreateGroup>
{
    public CreateGroupValidator() => RuleFor(c => c.Group.Name).NotEmpty().MaximumLength(200);
}
