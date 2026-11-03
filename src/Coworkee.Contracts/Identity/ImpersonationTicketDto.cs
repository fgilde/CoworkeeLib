namespace Coworkee.Contracts.Identity;

/// <summary>Proof for the auth server that the signed-in user may sign in as another user; valid for a minute and for this user only.</summary>
public sealed record ImpersonationTicketDto(string Ticket);
