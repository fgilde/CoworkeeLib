using System.ComponentModel;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Notifications;
using Coworkee.Core.Results;

namespace Coworkee.Notifications;

[Description("Sets whether and how the user gets one type of notification.")]
public sealed record SaveNotificationPreference(string Type, SaveNotificationPreferenceRequest Preference) : ICommand<Result<NotificationPreferenceDto>>;
