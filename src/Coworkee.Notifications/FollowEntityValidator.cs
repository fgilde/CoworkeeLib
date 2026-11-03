using FluentValidation;

namespace Coworkee.Notifications;

internal sealed class FollowEntityValidator : AbstractValidator<FollowEntity>
{
    public FollowEntityValidator() => RuleFor(c => c.Title).MaximumLength(200);
}
