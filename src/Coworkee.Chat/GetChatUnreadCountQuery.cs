using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Chat;
using Coworkee.Contracts.Notifications;
using Coworkee.Core.Results;

namespace Coworkee.Chat;

[RequiresPermission(ChatPermissions.Use)]
[Description("Counts the chat messages the signed-in user has not read yet.")]
public sealed record GetChatUnreadCountQuery : IQuery<Result<UnreadCountDto>>;
