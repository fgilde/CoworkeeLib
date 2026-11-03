using FluentValidation;

namespace Coworkee.Identity.Users;

internal sealed class CreateUserValidator : AbstractValidator<CreateUser>
{
    public CreateUserValidator()
    {
        RuleFor(c => c.User.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(c => c.User.Password).NotEmpty().When(c => c.User.Password is not null);
        RuleFor(c => c.User.FirstName).MaximumLength(100);
        RuleFor(c => c.User.LastName).MaximumLength(100);
    }
}
