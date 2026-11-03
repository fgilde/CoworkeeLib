using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Chat;
using Coworkee.Core.Results;

namespace Coworkee.Chat;

[RequiresPermission(ChatPermissions.Use)]
[Description("Adds people to a group chat the signed-in user is a member of.")]
public sealed record AddChatMembersCommand(Guid ConversationId, IReadOnlyList<Guid> UserIds) : ICommand<Result<ChatConversationDto>>;
