using FluentValidation;

namespace Coworkee.Identity.Groups;

internal sealed class SetGroupOwnersValidator : AbstractValidator<SetGroupOwners>
{
    public SetGroupOwnersValidator() => RuleFor(c => c.UserIds).NotNull();
}
