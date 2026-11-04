using FluentValidation;

namespace Coworkee.Identity.Permissions;

internal sealed class UpdateCustomPermissionValidator : AbstractValidator<UpdateCustomPermission>
{
    public UpdateCustomPermissionValidator()
    {
        RuleFor(c => c.Permission.DisplayName).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Permission.Description).MaximumLength(1000);
        RuleFor(c => c.Permission.Group).MaximumLength(100);
    }
}
