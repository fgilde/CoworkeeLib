using FluentValidation;

namespace Coworkee.Identity.Permissions;

internal sealed class AddGrantValidator : AbstractValidator<AddGrant>
{
    public AddGrantValidator()
    {
        RuleFor(c => c.ProviderType).IsInEnum();
        RuleFor(c => c.ProviderKey).NotEmpty();
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
    }
}

internal sealed class RemoveGrantValidator : AbstractValidator<RemoveGrant>
{
    public RemoveGrantValidator()
    {
        RuleFor(c => c.ProviderType).IsInEnum();
        RuleFor(c => c.ProviderKey).NotEmpty();
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
    }
}
