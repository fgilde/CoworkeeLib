using FluentValidation;

namespace Coworkee.Identity.Users;

internal sealed class StartImpersonationValidator : AbstractValidator<StartImpersonation>
{
    public StartImpersonationValidator() => RuleFor(c => c.UserId).NotEmpty();
}
