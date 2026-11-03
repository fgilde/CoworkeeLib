using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Notifications;

/// <summary>Who of the possible recipients gets a type of notification: nobody, everybody who may see it, or only the followers of its entity.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<NotificationScope>))]
public enum NotificationScope
{
    Off,
    All,
    Followed,
}
