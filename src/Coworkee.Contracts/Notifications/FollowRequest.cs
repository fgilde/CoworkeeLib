namespace Coworkee.Contracts.Notifications;

/// <param name="Title">How the entity shows in the list of followed ones, e.g. its number.</param>
public sealed record FollowRequest(string? Title = null);
