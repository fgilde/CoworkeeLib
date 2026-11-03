using FluentValidation;

namespace Coworkee.Identity.Groups;

internal sealed class SelectGroupsValidator : AbstractValidator<SelectGroups>
{
    public SelectGroupsValidator() => RuleFor(c => c.GroupIds).NotNull();
}
