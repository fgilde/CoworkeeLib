using Coworkee.Application.Messaging;
using Coworkee.Core.Results;

namespace Coworkee.Notifications;

public sealed record MarkAllNotificationsRead : ICommand<Result>;
