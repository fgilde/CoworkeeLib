using FluentValidation;

namespace Coworkee.Account.Email;

internal sealed class SetUserEmailValidator : AbstractValidator<SetUserEmail>
{
    public SetUserEmailValidator() => RuleFor(c => c.Email).NotEmpty().EmailAddress().MaximumLength(256);
}
