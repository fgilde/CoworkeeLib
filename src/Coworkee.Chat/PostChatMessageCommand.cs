using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Chat;
using Coworkee.Core.Results;

namespace Coworkee.Chat;

[RequiresPermission(ChatPermissions.Use)]
[Description("Sends a message to a chat: HTML with formatting and mentions (@person, #record), optionally as reply and with uploaded attachments.")]
public sealed record PostChatMessageCommand(Guid ConversationId, string Html, Guid? ReplyToId = null, IReadOnlyList<Guid>? AttachmentIds = null) : ICommand<Result<ChatMessageDto>>;
