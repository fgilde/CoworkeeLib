using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Chat;
using Coworkee.Core.Results;

namespace Coworkee.Chat;

[RequiresPermission(ChatPermissions.Use)]
[Description("Lists the people of the organisation to chat with, with the last direct message and the number of unread direct messages.")]
public sealed record GetChatContactsQuery : IQuery<Result<IReadOnlyList<ChatContactDto>>>;
