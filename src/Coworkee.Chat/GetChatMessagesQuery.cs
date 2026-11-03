using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Chat;
using Coworkee.Core.Results;

namespace Coworkee.Chat;

[RequiresPermission(ChatPermissions.Use)]
[Description("Reads the messages of a chat, newest last; Before pages back, Search keeps the messages containing every word.")]
public sealed record GetChatMessagesQuery(Guid ConversationId, DateTimeOffset? Before = null, string? Search = null, int Take = 50) : IQuery<Result<IReadOnlyList<ChatMessageDto>>>;
