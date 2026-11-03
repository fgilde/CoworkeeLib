using FluentValidation;

namespace Coworkee.Identity.Users;

internal sealed class UpdateUserValidator : AbstractValidator<UpdateUser>
{
    public UpdateUserValidator()
    {
        RuleFor(c => c.User.FirstName).MaximumLength(100);
        RuleFor(c => c.User.LastName).MaximumLength(100);
        RuleFor(c => c.User.UserName).NotEmpty().MaximumLength(256).When(c => c.User.UserName is not null);
        RuleFor(c => c.User.PhoneNumber).MaximumLength(50);
        RuleFor(c => c.User.Address!.Street).MaximumLength(200).When(c => c.User.Address is not null);
        RuleFor(c => c.User.Address!.ZipCode).MaximumLength(20).When(c => c.User.Address is not null);
        RuleFor(c => c.User.Address!.City).MaximumLength(100).When(c => c.User.Address is not null);
        RuleFor(c => c.User.Address!.Country).MaximumLength(100).When(c => c.User.Address is not null);
    }
}
