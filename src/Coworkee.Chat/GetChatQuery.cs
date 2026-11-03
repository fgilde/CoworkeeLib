using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Chat;
using Coworkee.Core.Results;

namespace Coworkee.Chat;

[RequiresPermission(ChatPermissions.Use)]
[Description("Reads one chat of the signed-in user with its members.")]
public sealed record GetChatQuery(Guid ConversationId) : IQuery<Result<ChatConversationDto>>;
