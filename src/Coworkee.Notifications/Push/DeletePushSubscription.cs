using Coworkee.Application.Messaging;
using Coworkee.Core.Results;

namespace Coworkee.Notifications.Push;

[AiTool(Exclude = true)]
public sealed record DeletePushSubscription(Guid Id) : ICommand<Result>;
