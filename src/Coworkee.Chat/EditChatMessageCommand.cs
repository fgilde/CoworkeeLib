using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Chat;
using Coworkee.Core.Results;

namespace Coworkee.Chat;

[RequiresPermission(ChatPermissions.Use)]
[Description("Changes the text of an own chat message; it shows as edited.")]
public sealed record EditChatMessageCommand(Guid MessageId, string Html) : ICommand<Result<ChatMessageDto>>;
