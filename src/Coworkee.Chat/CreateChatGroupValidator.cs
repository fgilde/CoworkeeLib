using FluentValidation;

namespace Coworkee.Chat;

internal sealed class CreateChatGroupValidator : AbstractValidator<CreateChatGroupCommand>
{
    public CreateChatGroupValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(ChatLimits.NameLength);
        RuleFor(c => c.MemberIds).NotEmpty().Must(ids => ids.Count < ChatLimits.MaxMembers);
    }
}
