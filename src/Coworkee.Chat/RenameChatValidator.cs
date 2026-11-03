using FluentValidation;

namespace Coworkee.Chat;

internal sealed class RenameChatValidator : AbstractValidator<RenameChatCommand>
{
    public RenameChatValidator() => RuleFor(c => c.Name).NotEmpty().MaximumLength(ChatLimits.NameLength);
}
