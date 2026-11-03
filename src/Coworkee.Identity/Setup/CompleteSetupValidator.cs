using FluentValidation;

namespace Coworkee.Identity.Setup;

internal sealed class CompleteSetupValidator : AbstractValidator<CompleteSetup>
{
    public CompleteSetupValidator()
    {
        RuleFor(c => c.Request.SetupToken).NotEmpty();
        RuleFor(c => c.Request.TenantName).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Request.AdminEmail).NotEmpty().EmailAddress();
        RuleFor(c => c.Request.AdminPassword).NotEmpty();
    }
}
