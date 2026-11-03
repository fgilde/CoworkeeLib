using Coworkee.Application.Messaging;
using Coworkee.Contracts.Notifications;
using Coworkee.Core.Results;

namespace Coworkee.Notifications.Push;

[AiTool(Exclude = true)]
public sealed record GetPushSettings : IQuery<Result<PushSettingsDto>>;
