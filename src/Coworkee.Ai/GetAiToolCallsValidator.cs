namespace Coworkee.Ai;

internal sealed class GetAiToolCallsValidator : FluentValidation.AbstractValidator<GetAiToolCalls>
{
    public GetAiToolCallsValidator() => RuleFor(q => q.Page).SetValidator(new Coworkee.Application.Paging.PageRequestValidator());
}
