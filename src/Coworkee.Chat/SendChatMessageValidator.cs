using FluentValidation;

namespace Coworkee.Chat;

internal sealed class SendChatMessageValidator : AbstractValidator<SendChatMessageCommand>
{
    public SendChatMessageValidator() => RuleFor(c => c.Text).NotEmpty().MaximumLength(ChatLimits.TextLength);
}
