using Coworkee.Application.Messaging;
using Coworkee.Core.Results;

namespace Coworkee.Notifications;

public sealed record MarkNotificationUnread(Guid Id) : ICommand<Result>;
