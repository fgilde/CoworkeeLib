using FluentValidation;

namespace Coworkee.Identity.Roles;

internal sealed class UpdateRoleValidator : AbstractValidator<UpdateRole>
{
    public UpdateRoleValidator() => RuleFor(c => c.Role.Name).NotEmpty().MaximumLength(256);
}
