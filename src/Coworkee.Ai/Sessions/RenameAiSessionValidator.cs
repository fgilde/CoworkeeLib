using FluentValidation;

namespace Coworkee.Ai.Sessions;

internal sealed class RenameAiSessionValidator : AbstractValidator<RenameAiSession>
{
    public RenameAiSessionValidator() => RuleFor(c => c.Title).NotEmpty().MaximumLength(AiSessionLimits.Title);
}
