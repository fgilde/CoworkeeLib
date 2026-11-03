using FluentValidation;

namespace Coworkee.Identity.Groups;

internal sealed class UpdateGroupValidator : AbstractValidator<UpdateGroup>
{
    public UpdateGroupValidator() => RuleFor(c => c.Group.Name).NotEmpty().MaximumLength(200);
}
