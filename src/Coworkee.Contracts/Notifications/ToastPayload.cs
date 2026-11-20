namespace Coworkee.Contracts.Notifications;

/// <summary>
/// A transient notification (<see cref="Type"/> <see cref="Event"/> on the realtime topics of its target): no row, no mail; the Blazor shell shows it as a
/// snackbar. With <see cref="Arguments"/> (also empty) <see cref="Title"/> and <see cref="Body"/> are English texts with {0} placeholders the client translates.
/// </summary>
public sealed record ToastPayload(
    string Title, string? Body = null, NotificationSeverity Severity = NotificationSeverity.Info, string? Link = null, IReadOnlyList<string>? Arguments = null,
    string? Type = null)
{
    /// <summary>The realtime event type of a toast.</summary>
    public const string Event = "notification.toast";
}
