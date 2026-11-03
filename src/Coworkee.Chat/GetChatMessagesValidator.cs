using FluentValidation;

namespace Coworkee.Chat;

internal sealed class GetChatMessagesValidator : AbstractValidator<GetChatMessagesQuery>
{
    public GetChatMessagesValidator()
    {
        RuleFor(q => q.Take).InclusiveBetween(1, 200);
        RuleFor(q => q.Search).MaximumLength(200);
    }
}
