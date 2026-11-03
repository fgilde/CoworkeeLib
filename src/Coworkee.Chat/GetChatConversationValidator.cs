using FluentValidation;

namespace Coworkee.Chat;

internal sealed class GetChatConversationValidator : AbstractValidator<GetChatConversationQuery>
{
    public GetChatConversationValidator() => RuleFor(q => q.Take).InclusiveBetween(1, 200);
}
