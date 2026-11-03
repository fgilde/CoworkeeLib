using Coworkee.Application.Messaging;
using Coworkee.Core.Results;

namespace Coworkee.Notifications;

public sealed record MarkNotificationRead(Guid Id) : ICommand<Result>;
