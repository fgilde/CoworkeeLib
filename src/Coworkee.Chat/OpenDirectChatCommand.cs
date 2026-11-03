using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Chat;
using Coworkee.Core.Results;

namespace Coworkee.Chat;

[RequiresPermission(ChatPermissions.Use)]
[Description("Opens the direct chat with a person, creating it on first use.")]
public sealed record OpenDirectChatCommand(Guid UserId) : ICommand<Result<ChatConversationDto>>;
