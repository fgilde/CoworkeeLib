using FluentValidation;

namespace Coworkee.Social;

internal sealed class SetEntityTagsValidator : AbstractValidator<SetEntityTagsCommand>
{
    public SetEntityTagsValidator()
    {
        RuleFor(c => c.Tags).Must(t => t.Count <= 50).WithMessage("An entity carries at most 50 tags.");
        RuleForEach(c => c.Tags).NotEmpty().Must(t => t.Trim().Length <= SocialLimits.TagName).WithMessage($"A tag has at most {SocialLimits.TagName} characters.");
    }
}
