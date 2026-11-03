using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Chat;
using Coworkee.Core.Results;

namespace Coworkee.Chat;

[RequiresPermission(ChatPermissions.Use)]
[Description("Renames a group chat the signed-in user is a member of.")]
public sealed record RenameChatCommand(Guid ConversationId, string Name) : ICommand<Result<ChatConversationDto>>;
