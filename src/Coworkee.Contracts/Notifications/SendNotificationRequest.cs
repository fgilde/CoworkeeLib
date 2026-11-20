using System.Text.Json.Serialization;
using Coworkee.Contracts.Identity;

namespace Coworkee.Contracts.Notifications;

/// <summary>A notification for the users of <see cref="Recipients"/>, sent with <c>INotifier.SendAsync</c> (the rules for the actor and the preferences apply).</summary>
public sealed record SendNotificationRequest(UserSelection Recipients, string Title, string? Body = null)
{
    /// <summary>Makes title and body English texts with {0} placeholders that every reader sees in his language.</summary>
    public IReadOnlyList<string>? Arguments { get; init; }

    /// <summary>A path of the app (<c>/orders/1</c>) or an http(s) address.</summary>
    public string? Link { get; init; }

    public NotificationSeverity Severity { get; init; } = NotificationSeverity.Info;

    /// <summary>A row in the inbox of each recipient.</summary>
    public bool Stored { get; init; } = true;

    /// <summary>A snackbar for the recipients who are online now.</summary>
    public bool Toast { get; init; }

    public NotificationMail Mail { get; init; } = NotificationMail.ByPreference;

    /// <summary>The notification type (preferences, mail template); null for the generic one.</summary>
    public string? Type { get; init; }
}

/// <summary>When a notification goes out as mail, as the server's <c>MailDelivery</c>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<NotificationMail>))]
public enum NotificationMail
{
    /// <summary>As each recipient's preference for the type says.</summary>
    ByPreference,

    Never,

    /// <summary>To every recipient whose preference is not Off.</summary>
    Always,

    /// <summary>To every recipient without a realtime connection now.</summary>
    WhenOffline,
}
