namespace Coworkee.Contracts.Notifications;

/// <summary>Filters of the inbox; <paramref name="Done"/> null lists open and done ones.</summary>
/// <param name="Category">One category, or several separated by commas (<c>Shipment,Booking</c>).</param>
/// <param name="OldestFirst">The oldest first instead of the newest.</param>
public sealed record NotificationFilter(bool UnreadOnly = false, string? Category = null, bool? Done = null, string? Search = null, bool OldestFirst = false);
