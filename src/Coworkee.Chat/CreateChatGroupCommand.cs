using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Chat;
using Coworkee.Core.Results;

namespace Coworkee.Chat;

[RequiresPermission(ChatPermissions.Use)]
[Description("Creates a group chat with a name and the people in it; the creator is its owner.")]
public sealed record CreateChatGroupCommand(string Name, IReadOnlyList<Guid> MemberIds) : ICommand<Result<ChatConversationDto>>;
