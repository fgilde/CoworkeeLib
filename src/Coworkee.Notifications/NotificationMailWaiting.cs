using Coworkee.Domain;

namespace Coworkee.Notifications;

/// <summary>A notification mail to wait, written after the command that sent it committed.</summary>
public sealed record NotificationMailWaiting(
    Guid UserId, Guid? TenantId, string Type, string Key, string Title, string? Body, IReadOnlyList<string>? Arguments, string? Link, DateTimeOffset DueAt,
    string? MailModel = null) : IDomainEvent;
