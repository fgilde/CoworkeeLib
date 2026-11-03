using Coworkee.Application.Messaging;
using Coworkee.Core.Results;

namespace Coworkee.Notifications;

public sealed record DeleteNotification(Guid Id) : ICommand<Result>;
