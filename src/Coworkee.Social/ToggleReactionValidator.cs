using FluentValidation;

namespace Coworkee.Social;

internal sealed class ToggleReactionValidator : AbstractValidator<ToggleReactionCommand>
{
    public ToggleReactionValidator() =>
        RuleFor(c => c.Emoji).NotEmpty().MaximumLength(SocialLimits.Emoji).Must(e => e.All(c => c >= 128)).WithMessage("Use an emoji.");
}
