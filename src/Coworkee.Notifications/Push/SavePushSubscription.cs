using Coworkee.Application.Messaging;
using Coworkee.Contracts.Notifications;
using Coworkee.Core.Results;

namespace Coworkee.Notifications.Push;

/// <summary>Turns push on for the browser that sends it; the same browser signed in as someone else moves to that account.</summary>
[AiTool(Exclude = true)]
public sealed record SavePushSubscription(PushSubscriptionRequest Subscription) : ICommand<Result<PushDeviceDto>>;
