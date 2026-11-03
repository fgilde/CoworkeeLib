using FluentValidation;

namespace Coworkee.Ai;

internal sealed class ListAiModelsValidator : AbstractValidator<ListAiModels>
{
    public ListAiModelsValidator() => RuleFor(q => q.Draft).NotNull().SetValidator(new AiConnectionDraftValidator());
}
