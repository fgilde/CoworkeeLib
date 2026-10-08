using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Chat;
using Coworkee.Core.Results;
using FluentValidation;

namespace Coworkee.Chat;

[RequiresPermission(ChatPermissions.Use)]
[Description("Lists the people of the organisation to chat with, with the last message and the number of unread messages.")]
public sealed record GetChatContactsQuery : IQuery<Result<IReadOnlyList<ChatContactDto>>>;

[RequiresPermission(ChatPermissions.Use)]
[Description("Reads the chat with one person, newest last; Before pages back to older messages.")]
public sealed record GetChatConversationQuery(Guid UserId, DateTimeOffset? Before = null, int Take = 50) : IQuery<Result<IReadOnlyList<ChatMessageDto>>>;

[RequiresPermission(ChatPermissions.Use)]
[Description("Sends a chat message to a person of the organisation.")]
public sealed record SendChatMessageCommand(Guid ToUserId, string Text) : ICommand<Result<ChatMessageDto>>;

[RequiresPermission(ChatPermissions.Use)]
[AiTool(Exclude = true)]
public sealed record MarkChatReadCommand(Guid UserId) : ICommand<Result>;

internal sealed class SendChatMessageValidator : AbstractValidator<SendChatMessageCommand>
{
    public SendChatMessageValidator() => RuleFor(c => c.Text).NotEmpty().MaximumLength(ChatLimits.TextLength);
}

internal sealed class GetChatConversationValidator : AbstractValidator<GetChatConversationQuery>
{
    public GetChatConversationValidator() => RuleFor(q => q.Take).InclusiveBetween(1, 200);
}
