using FluentValidation;

namespace Coworkee.Localization.Features.Texts;

internal sealed class SetTranslationValidator : AbstractValidator<SetTranslationCommand>
{
    public SetTranslationValidator()
    {
        RuleFor(c => c.Translation.Culture).NotEmpty().MaximumLength(20);
        RuleFor(c => c.Translation.Key).NotEmpty().MaximumLength(500);
        RuleFor(c => c.Translation.Value).MaximumLength(4000);
    }
}
