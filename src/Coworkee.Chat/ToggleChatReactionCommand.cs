using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Chat;
using Coworkee.Core.Results;

namespace Coworkee.Chat;

[RequiresPermission(ChatPermissions.Use)]
[Description("Adds an emoji reaction to a chat message, or removes it when the signed-in user already reacted with it.")]
public sealed record ToggleChatReactionCommand(Guid MessageId, string Emoji) : ICommand<Result<ChatMessageDto>>;
