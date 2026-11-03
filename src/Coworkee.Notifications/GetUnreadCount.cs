using Coworkee.Application.Messaging;
using Coworkee.Contracts.Notifications;
using Coworkee.Core.Results;

namespace Coworkee.Notifications;

public sealed record GetUnreadCount : IQuery<Result<UnreadCountDto>>;
