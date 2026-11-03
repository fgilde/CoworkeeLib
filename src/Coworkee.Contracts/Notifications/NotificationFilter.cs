namespace Coworkee.Contracts.Notifications;

/// <summary>Filters of the inbox; <paramref name="Done"/> null lists open and done ones.</summary>
public sealed record NotificationFilter(bool UnreadOnly = false, string? Category = null, bool? Done = null, string? Search = null);
