using Coworkee.Domain;

namespace Coworkee.Notifications;

[NotAudited]
public sealed class Notification : AggregateRoot, IHasRealtimeTopics
{
    public Guid UserId { get; set; }

    public Guid? TenantId { get; set; }

    public required string Type { get; set; }

    public required string Title { get; set; }

    public string? Body { get; set; }

    public string? Link { get; set; }

    /// <summary>Set for localizable notifications: <see cref="Title"/> and <see cref="Body"/> are texts with placeholders the reader's language translates.</summary>
    public List<string>? Arguments { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ReadAt { get; set; }

    /// <summary>The category of the registered type, for the inbox filter and counts.</summary>
    public string? Category { get; set; }

    /// <summary>Marked done: the notification leaves the open inbox (and counts as read).</summary>
    public DateTimeOffset? DoneAt { get; set; }

    /// <summary>Also sent as a mail of its own; the daily digest leaves it out.</summary>
    public bool Mailed { get; set; }

    public IEnumerable<string> RealtimeTopics => [Contracts.Realtime.RealtimeTopics.User(UserId)];

    /// <summary>The text in English, placeholders filled.</summary>
    public static string? Format(string? text, IReadOnlyList<string>? arguments) =>
        text is null || arguments is null ? text : string.Format(System.Globalization.CultureInfo.InvariantCulture, text, [.. arguments]);

    internal void OfferPush() => Raise(new Push.NotificationCreated(Id));
}
