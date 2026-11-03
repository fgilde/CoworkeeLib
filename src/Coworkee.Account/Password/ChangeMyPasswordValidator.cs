using FluentValidation;

namespace Coworkee.Account.Password;

internal sealed class ChangeMyPasswordValidator : AbstractValidator<ChangeMyPassword>
{
    public ChangeMyPasswordValidator()
    {
        RuleFor(c => c.CurrentPassword).NotEmpty().MaximumLength(256);
        RuleFor(c => c.NewPassword).NotEmpty().MaximumLength(256);
    }
}
