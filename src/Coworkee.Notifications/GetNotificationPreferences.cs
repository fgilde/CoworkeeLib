using System.ComponentModel;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Notifications;
using Coworkee.Core.Results;

namespace Coworkee.Notifications;

[Description("Lists the notification types with the user's choice: Off, All or Followed, in the app and by e-mail.")]
public sealed record GetNotificationPreferences : IQuery<Result<IReadOnlyList<NotificationPreferenceDto>>>;
