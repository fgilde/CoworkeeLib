using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Notifications;

/// <summary>How a toast looks (the snackbar's colour and icon).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<NotificationSeverity>))]
public enum NotificationSeverity
{
    Normal,
    Info,
    Success,
    Warning,
    Error,
}
