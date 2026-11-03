using Coworkee.Contracts.Notifications;

namespace Coworkee.Notifications;

/// <summary>A kind of notification users choose for themselves: whether they get it (Off, All, Followed) and how (in the app, by e-mail).</summary>
/// <param name="Name">English text for the preferences page, a localization key.</param>
/// <param name="EmailDelay">Mails wait this long; another notification of the same type and entity before that replaces the waiting one (one mail when things settle).</param>
public sealed record NotificationType(
    string Type, string? Category, string Name, NotificationScope Scope = NotificationScope.All, bool InApp = true, bool Email = false, TimeSpan? EmailDelay = null)
{
    /// <summary>
    /// The mail template (Coworkee.Mailing, editable on the mail templates page) of this type's mails; it gets <see cref="NotificationMessage.MailModel"/>
    /// with <c>user</c> and <c>notification</c>. Null, or a name no template has: the generic mail <c>Notifications.Notification</c>.
    /// </summary>
    public string? MailTemplate { get; init; }

    /// <summary>
    /// The mail delay of the sending tenant, asked when a mail is queued (e.g. from a feature: the services are those of the sender's scope).
    /// Null from it, or no resolver: <see cref="EmailDelay"/>.
    /// </summary>
    public Func<IServiceProvider, CancellationToken, Task<TimeSpan?>>? ResolveEmailDelay { get; init; }

    internal async Task<TimeSpan?> EmailDelayAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        ResolveEmailDelay is { } resolve && await resolve(services, cancellationToken) is { } delay ? delay : EmailDelay;
}
