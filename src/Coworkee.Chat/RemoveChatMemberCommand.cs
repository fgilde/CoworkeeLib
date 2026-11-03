using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Chat;
using Coworkee.Core.Results;

namespace Coworkee.Chat;

[RequiresPermission(ChatPermissions.Use)]
[Description("Removes a person from a group chat: anyone may leave, only the owner removes others.")]
public sealed record RemoveChatMemberCommand(Guid ConversationId, Guid UserId) : ICommand<Result>;
