using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Chat;
using Coworkee.Core.Results;

namespace Coworkee.Chat;

[RequiresPermission(ChatPermissions.Use)]
[AiTool(Exclude = true)]
public sealed record ChatTypingCommand(Guid ConversationId) : ICommand<Result>;
