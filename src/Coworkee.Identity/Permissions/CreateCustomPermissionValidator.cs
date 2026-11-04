using FluentValidation;

namespace Coworkee.Identity.Permissions;

internal sealed class CreateCustomPermissionValidator : AbstractValidator<CreateCustomPermission>
{
    public CreateCustomPermissionValidator()
    {
        RuleFor(c => c.Permission.Name).NotEmpty().MaximumLength(200).Matches(CustomPermissionRules.NamePattern())
            .WithMessage("The name starts with \"Custom.\" followed by letters, digits, '-' or '_', parts separated by dots.");
        RuleFor(c => c.Permission.DisplayName).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Permission.Description).MaximumLength(1000);
        RuleFor(c => c.Permission.Group).MaximumLength(100);
    }
}
