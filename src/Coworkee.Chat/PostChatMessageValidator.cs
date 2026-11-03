using FluentValidation;

namespace Coworkee.Chat;

internal sealed class PostChatMessageValidator : AbstractValidator<PostChatMessageCommand>
{
    public PostChatMessageValidator()
    {
        RuleFor(c => c.Html).NotNull().MaximumLength(ChatLimits.HtmlLength);
        RuleFor(c => c.AttachmentIds).Must(ids => ids is null || ids.Count <= ChatLimits.MaxAttachments);
    }
}
