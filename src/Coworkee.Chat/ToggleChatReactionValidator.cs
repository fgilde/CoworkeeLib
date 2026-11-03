using FluentValidation;

namespace Coworkee.Chat;

internal sealed class ToggleChatReactionValidator : AbstractValidator<ToggleChatReactionCommand>
{
    public ToggleChatReactionValidator() => RuleFor(c => c.Emoji).NotEmpty().MaximumLength(ChatLimits.EmojiLength).Must(e => e.All(c => c >= 128))
        .WithMessage("Use an emoji.");
}
