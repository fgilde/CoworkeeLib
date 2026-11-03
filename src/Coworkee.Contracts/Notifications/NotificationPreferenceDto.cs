namespace Coworkee.Contracts.Notifications;

/// <summary>A type of notification with the user's choice and the type's defaults.</summary>
public sealed record NotificationPreferenceDto(
    string Type, string? Category, string Name, NotificationScope Scope, bool InApp, bool Email, NotificationScope DefaultScope, bool DefaultInApp, bool DefaultEmail);
