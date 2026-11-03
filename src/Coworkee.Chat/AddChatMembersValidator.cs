using FluentValidation;

namespace Coworkee.Chat;

internal sealed class AddChatMembersValidator : AbstractValidator<AddChatMembersCommand>
{
    public AddChatMembersValidator() => RuleFor(c => c.UserIds).NotEmpty().Must(ids => ids.Count < ChatLimits.MaxMembers);
}
