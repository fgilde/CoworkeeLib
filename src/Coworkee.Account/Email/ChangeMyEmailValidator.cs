using FluentValidation;

namespace Coworkee.Account.Email;

internal sealed class ChangeMyEmailValidator : AbstractValidator<ChangeMyEmail>
{
    public ChangeMyEmailValidator() => RuleFor(c => c.NewEmail).NotEmpty().EmailAddress().MaximumLength(256);
}
