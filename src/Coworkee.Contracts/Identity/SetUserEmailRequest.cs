namespace Coworkee.Contracts.Identity;

/// <summary>An administrator changes a user's address; unconfirmed addresses get a confirmation mail.</summary>
public sealed record SetUserEmailRequest(string Email, bool Confirmed);
