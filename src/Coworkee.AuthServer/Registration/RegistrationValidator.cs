using System.Net.Mail;
using Coworkee.Contracts.Configuration;
using FluentValidation;
using static Coworkee.AuthServer.AuthTexts;

namespace Coworkee.AuthServer.Registration;

/// <summary>One rule set per wizard step; password strength comes from the Identity password validators.</summary>
public sealed class RegistrationValidator : AbstractValidator<RegistrationInput>
{
    public RegistrationValidator(RegistrationOptions options, IReadOnlyCollection<Guid> selectableRoles)
    {
        RuleSet(RegistrationSteps.Account, () =>
        {
            RuleFor(i => i.Email).NotEmpty().MaximumLength(256).Must(BeAnAddress).WithMessage(_ => T("Enter a valid email address."))
                .Must(e => Wildcards.Allows(options.AllowedEmails, e)).WithMessage(_ => T("This email address cannot be registered."));
            RuleFor(i => i.Password).NotEmpty().WithMessage(_ => T("Choose a password."));
            RuleFor(i => i.ConfirmPassword).Equal(i => i.Password).WithMessage(_ => T("The passwords do not match."));
        });
        RuleSet(RegistrationSteps.Profile, () =>
        {
            RuleFor(i => i.FirstName).NotEmpty().WithMessage(_ => T("Enter your first name.")).MaximumLength(100);
            RuleFor(i => i.LastName).NotEmpty().WithMessage(_ => T("Enter your last name.")).MaximumLength(100);
            RuleFor(i => i.PhoneNumber).MaximumLength(50);
            RuleFor(i => i.Street).MaximumLength(200);
            RuleFor(i => i.ZipCode).MaximumLength(20);
            RuleFor(i => i.City).MaximumLength(100);
            RuleFor(i => i.Country).MaximumLength(100);
            When(_ => options.RequireAddress, () =>
                RuleFor(i => new[] { i.Street, i.ZipCode, i.City, i.Country }).Must(a => a.All(p => !string.IsNullOrWhiteSpace(p)))
                    .WithName("Address").WithMessage(_ => T("Enter your complete address.")));
        });
        RuleSet(RegistrationSteps.Roles, () =>
            RuleFor(i => i.RoleIds).Must(ids => ids.All(selectableRoles.Contains)).WithMessage(_ => T("Choose one of the offered roles.")));
    }

    private static bool BeAnAddress(string email) => MailAddress.TryCreate(email, out var address) && address.Address == email;
}
