using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Chat;
using Coworkee.Core.Results;

namespace Coworkee.Chat;

[RequiresPermission(ChatPermissions.Use)]
[Description("Reads the direct chat with one person, newest last; Before pages back to older messages.")]
public sealed record GetChatConversationQuery(Guid UserId, DateTimeOffset? Before = null, int Take = 50) : IQuery<Result<IReadOnlyList<ChatMessageDto>>>;
