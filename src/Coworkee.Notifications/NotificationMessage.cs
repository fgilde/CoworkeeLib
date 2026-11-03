namespace Coworkee.Notifications;

/// <summary>
/// A notification to send. <see cref="Recipients"/> are addressed directly; with <see cref="EntityType"/> and <see cref="EntityId"/> the followers of the entity get it,
/// with <see cref="Audience"/> (the users allowed to see it) everybody whose preference for the type is All. The preferences of a registered type decide
/// per user whether and how: in the app, by e-mail (after the type's delay).
/// </summary>
public sealed record NotificationMessage(string Type, string Title, string? Body = null)
{
    /// <summary>Set for localizable notifications: title and body are English texts with {0} placeholders for these.</summary>
    public IReadOnlyList<string>? Arguments { get; init; }

    public string? Link { get; init; }

    public IReadOnlyCollection<Guid>? Recipients { get; init; }

    public string? EntityType { get; init; }

    public Guid? EntityId { get; init; }

    /// <summary>
    /// Who may see the entity: users with the preference All get it; followers outside it do not. Null: every follower is checked as himself against the
    /// entity's set (types registered with <c>Followable&lt;TEntity&gt;</c>); followers of types registered with <c>Followable(string)</c> then get nothing.
    /// </summary>
    public IReadOnlyCollection<Guid>? Audience { get; init; }

    /// <summary>
    /// The model of the type's <see cref="NotificationType.MailTemplate"/>: an object or a dictionary, stored as JSON with a waiting mail (a newer one of the
    /// same entity replaces it). Its members are the template's top-level names (<c>OldValue</c> becomes <c>old_value</c>), next to <c>user</c> and
    /// <c>notification</c>, which it cannot set (nor layout variables or Scriban builtins); at most 32 KB as JSON. JSON has no dates, so format them as text.
    /// </summary>
    public object? MailModel { get; init; }
}
