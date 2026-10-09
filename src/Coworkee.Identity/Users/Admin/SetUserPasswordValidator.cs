using FluentValidation;

namespace Coworkee.Identity.Users.Admin;

internal sealed class SetUserPasswordValidator : AbstractValidator<SetUserPassword>
{
    public SetUserPasswordValidator() => RuleFor(c => c.Password).NotEmpty();
}
