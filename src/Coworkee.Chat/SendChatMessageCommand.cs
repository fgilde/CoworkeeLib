using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Chat;
using Coworkee.Core.Results;

namespace Coworkee.Chat;

[RequiresPermission(ChatPermissions.Use)]
[Description("Sends a plain text chat message to a person of the organisation.")]
public sealed record SendChatMessageCommand(Guid ToUserId, string Text) : ICommand<Result<ChatMessageDto>>;
