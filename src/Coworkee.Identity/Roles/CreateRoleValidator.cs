using FluentValidation;

namespace Coworkee.Identity.Roles;

internal sealed class CreateRoleValidator : AbstractValidator<CreateRole>
{
    public CreateRoleValidator() => RuleFor(c => c.Role.Name).NotEmpty().MaximumLength(256);
}
