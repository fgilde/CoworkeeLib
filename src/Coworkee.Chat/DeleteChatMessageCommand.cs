using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Chat;
using Coworkee.Core.Results;

namespace Coworkee.Chat;

[RequiresPermission(ChatPermissions.Use)]
[Description("Deletes an own chat message with its attachments; the chat shows that it was deleted.")]
public sealed record DeleteChatMessageCommand(Guid MessageId) : ICommand<Result>;
