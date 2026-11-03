using FluentValidation;

namespace Coworkee.Identity.Users.Privacy;

internal sealed class DeleteMyAccountValidator : AbstractValidator<DeleteMyAccount>
{
    public DeleteMyAccountValidator() => RuleFor(c => c.Email).NotEmpty();
}
