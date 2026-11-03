using FluentValidation;

namespace Coworkee.Ai;

internal sealed class TestAiConnectionValidator : AbstractValidator<TestAiConnection>
{
    public TestAiConnectionValidator() => RuleFor(q => q.Draft).NotNull().SetValidator(new AiConnectionDraftValidator());
}
