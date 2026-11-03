using FluentValidation;

namespace Coworkee.Ai.Sessions;

internal sealed class CreateAiSessionValidator : AbstractValidator<CreateAiSession>
{
    public CreateAiSessionValidator() => RuleFor(c => c.Title).MaximumLength(AiSessionLimits.Title);
}
