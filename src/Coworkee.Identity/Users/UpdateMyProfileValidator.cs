using FluentValidation;

namespace Coworkee.Identity.Users;

internal sealed class UpdateMyProfileValidator : AbstractValidator<UpdateMyProfile>
{
    public UpdateMyProfileValidator()
    {
        RuleFor(c => c.Profile.FirstName).MaximumLength(100);
        RuleFor(c => c.Profile.LastName).MaximumLength(100);
        RuleFor(c => c.Profile.PhoneNumber).MaximumLength(50);
        RuleFor(c => c.Profile.Address!.Street).MaximumLength(200).When(c => c.Profile.Address is not null);
        RuleFor(c => c.Profile.Address!.ZipCode).MaximumLength(20).When(c => c.Profile.Address is not null);
        RuleFor(c => c.Profile.Address!.City).MaximumLength(100).When(c => c.Profile.Address is not null);
        RuleFor(c => c.Profile.Address!.Country).MaximumLength(100).When(c => c.Profile.Address is not null);
    }
}
