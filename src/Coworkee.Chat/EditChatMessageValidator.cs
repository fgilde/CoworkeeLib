using FluentValidation;

namespace Coworkee.Chat;

internal sealed class EditChatMessageValidator : AbstractValidator<EditChatMessageCommand>
{
    public EditChatMessageValidator() => RuleFor(c => c.Html).NotEmpty().MaximumLength(ChatLimits.HtmlLength);
}
