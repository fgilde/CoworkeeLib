using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Chat;
using Coworkee.Core.Results;

namespace Coworkee.Chat;

[RequiresPermission(ChatPermissions.Use)]
[Description("Lists the direct and group chats of the signed-in user, newest first, with the last message and the number of unread messages.")]
public sealed record GetChatsQuery : IQuery<Result<IReadOnlyList<ChatConversationDto>>>;
