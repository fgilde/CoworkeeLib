using FluentValidation;

namespace Coworkee.Identity.Users;

internal sealed class AddUserRoleValidator : AbstractValidator<AddUserRole>
{
    public AddUserRoleValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.RoleId).NotEmpty();
    }
}

internal sealed class RemoveUserRoleValidator : AbstractValidator<RemoveUserRole>
{
    public RemoveUserRoleValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.RoleId).NotEmpty();
    }
}
